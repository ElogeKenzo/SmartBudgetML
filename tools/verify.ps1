param(
    [switch]$SkipAndroid
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot

try {
    $failures = [System.Collections.Generic.List[string]]::new()
    $commitShort = (git rev-parse --short HEAD 2>$null).Trim()
    $commitFull = (git rev-parse HEAD 2>$null).Trim()
    $branch = (git rev-parse --abbrev-ref HEAD 2>$null).Trim()
    $dateUtc = (Get-Date).ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'")

    # 1. Build & Run Tests (minimal output, TRX for exact counts)
    $artifactsDir = Join-Path $repoRoot "artifacts"
    New-Item -ItemType Directory -Force -Path $artifactsDir | Out-Null
    $trxPath = Join-Path $artifactsDir "test-results.trx"
    if (Test-Path $trxPath) { Remove-Item $trxPath -Force }

    $ErrorActionPreference = "Continue"
    $null = & dotnet build SmartBudgetML.Tests/SmartBudgetML.Tests.csproj -c Release --nologo -v quiet
    $testBuildOk = ($LASTEXITCODE -eq 0)
    if (-not $testBuildOk) { $failures.Add("Build Tests failed") }

    $null = & dotnet test SmartBudgetML.Tests/SmartBudgetML.Tests.csproj -c Release --no-build --nologo -v quiet --logger "trx;LogFileName=$trxPath" -- xUnit.MaxParallelThreads=1
    $testRunOk = ($LASTEXITCODE -eq 0)
    if (-not $testRunOk) { $failures.Add("Unit Tests failed") }

    $totalTests = 0; $passedTests = 0; $failedTests = 0; $skippedTests = 0
    if (Test-Path $trxPath) {
        [xml]$trx = Get-Content $trxPath
        $counters = $trx.TestRun.ResultSummary.Counters
        $totalTests = [int]$counters.total
        $passedTests = [int]$counters.passed
        $failedTests = [int]$counters.failed
        $skippedTests = [int]$counters.total - [int]$counters.executed
    }

    # 2. Build Android Release (unless -SkipAndroid)
    $androidStatus = "SKIPPED (-SkipAndroid)"
    if (-not $SkipAndroid) {
        $null = & dotnet build SmartBudgetML/SmartBudgetML.csproj -f net10.0-android -c Release --nologo -v quiet
        if ($LASTEXITCODE -eq 0) {
            $androidStatus = "PASS"
        } else {
            $androidStatus = "FAIL"
            $failures.Add("Android Release build failed")
        }
    }

    # 2b. Android artifact gates (same scripts as the CI build-android job)
    $selfTestAbsentStr = "SKIPPED"
    $align16kStr = "SKIPPED"
    if ($androidStatus -eq "PASS") {
        $ilDll = Join-Path $repoRoot "SmartBudgetML/obj/Release/net10.0-android/SmartBudgetML.dll"
        $null = & python -I (Join-Path $repoRoot "tools/check_selftest_absent.py") $ilDll
        $selfTestAbsentStr = if ($LASTEXITCODE -eq 0) { "PASS" } else { "FAIL" }
        if ($selfTestAbsentStr -ne "PASS") { $failures.Add("SB_SELFTEST code found in the Release build") }

        $apk = Join-Path $repoRoot "SmartBudgetML/bin/Release/net10.0-android/com.smartbudgetml.app-Signed.apk"
        $null = & python -I (Join-Path $repoRoot "tools/check_elf_alignment.py") $apk
        $align16kStr = if ($LASTEXITCODE -eq 0) { "PASS" } else { "FAIL" }
        if ($align16kStr -ne "PASS") { $failures.Add("Native libraries not aligned on 16 KB pages") }
    }
    $ErrorActionPreference = "Stop"

    # 3. Guardrails
    $srcFiles = Get-ChildItem -Path (Join-Path $repoRoot "SmartBudgetML") -Recurse -Include *.cs,*.razor |
        Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\' }

    $emptyCatchCount = @($srcFiles | Select-String -Pattern 'catch\s*(?:\([^\)]*\))?\s*\{\s*\}').Count
    $guardEmptyCatch = ($emptyCatchCount -eq 0)
    if (-not $guardEmptyCatch) { $failures.Add("Empty catch blocks found: $emptyCatchCount") }

    $syncBlockCount = @($srcFiles | Select-String -Pattern 'GetAwaiter\(\)\.GetResult\(\)').Count
    $guardNoSyncBlock = ($syncBlockCount -eq 0)
    if (-not $guardNoSyncBlock) { $failures.Add("GetAwaiter().GetResult() found: $syncBlockCount") }

    $vaultKeyFileCount = @($srcFiles | Select-String -Pattern 'sb_vault_key').Count
    $guardNoVaultKeyFile = ($vaultKeyFileCount -eq 0)
    if (-not $guardNoVaultKeyFile) { $failures.Add("Plaintext key fallback .sb_vault_key referenced in app code: $vaultKeyFileCount") }

    $manifestPath = Join-Path $repoRoot "SmartBudgetML/Platforms/Android/AndroidManifest.xml"
    [xml]$manifestXml = Get-Content $manifestPath
    $permissions = @($manifestXml.manifest.'uses-permission' | ForEach-Object { $_.GetAttribute("name", "http://schemas.android.com/apk/res/android") })
    $extraPerms = @($permissions | Where-Object { $_ -and $_ -ne "android.permission.USE_BIOMETRIC" })
    $guardManifestPerms = ($extraPerms.Count -eq 0 -and ($permissions -contains "android.permission.USE_BIOMETRIC"))
    if (-not $guardManifestPerms) { $failures.Add("Invalid AndroidManifest permissions: $($permissions -join ', ')") }

    $readSmsCount = @($srcFiles | Select-String -Pattern 'android\.permission\.READ_SMS|android\.permission\.RECEIVE_SMS').Count
    $manifestReadSms = @(Select-String -Path $manifestPath -Pattern 'READ_SMS|RECEIVE_SMS').Count
    $guardNoReadSms = (($readSmsCount + $manifestReadSms) -eq 0)
    if (-not $guardNoReadSms) { $failures.Add("READ_SMS/RECEIVE_SMS reference found") }

    $trackedFiles = @(git ls-files)
    $leakedSecrets = @($trackedFiles | Where-Object { $_ -match '\.(jks|keystore|pfx|p12)$' -or $_ -match '(^|/)\.sb_vault_key$' })
    $guardNoKeystoreTracked = ($leakedSecrets.Count -eq 0)
    if (-not $guardNoKeystoreTracked) { $failures.Add("Sensitive files tracked in git: $($leakedSecrets -join ', ')") }

    # Count localization keys
    [xml]$frResx = Get-Content (Join-Path $repoRoot "SmartBudgetML/Resources/Strings/AppStrings.fr.resx")
    [xml]$enResx = Get-Content (Join-Path $repoRoot "SmartBudgetML/Resources/Strings/AppStrings.en.resx")
    $frKeyCount = @($frResx.root.data).Count
    $enKeyCount = @($enResx.root.data).Count

    # 4. Copy artifacts/ml-eval.json & Generate docs/generated/STATUS.md
    $genDir = Join-Path $repoRoot "docs/generated"
    New-Item -ItemType Directory -Force -Path $genDir | Out-Null
    $mlEvalSrc = Join-Path $artifactsDir "ml-eval.json"
    $mlEvalDst = Join-Path $genDir "ml-eval.json"
    if (Test-Path $mlEvalSrc) {
        Copy-Item $mlEvalSrc $mlEvalDst -Force
        $ml = Get-Content $mlEvalSrc -Raw | ConvertFrom-Json
    } else {
        $failures.Add("artifacts/ml-eval.json missing")
    }

    $statusMdPath = Join-Path $genDir "STATUS.md"
    $invariant = [System.Globalization.CultureInfo]::InvariantCulture
    $rulesAccPct = ([double]$ml.rulesOnly.microAccuracy * 100.0).ToString("F2", $invariant) + " %"
    $rulesF1Pct  = ([double]$ml.rulesOnly.macroF1 * 100.0).ToString("F2", $invariant) + " %"
    $mlAccPct    = ([double]$ml.mlOnly.microAccuracy * 100.0).ToString("F2", $invariant) + " %"
    $mlF1Pct     = ([double]$ml.mlOnly.macroF1 * 100.0).ToString("F2", $invariant) + " %"
    $hybAccPct   = ([double]$ml.hybrid.microAccuracy * 100.0).ToString("F2", $invariant) + " %"
    $hybF1Pct    = ([double]$ml.hybrid.macroF1 * 100.0).ToString("F2", $invariant) + " %"

    $trainSamples = $ml.trainingSampleCount
    $holdoutSamples = $ml.holdoutSampleCount
    $catCount = $ml.categoryCount
    $avgLatMs = $ml.benchmark.avgInferenceMs
    $p95LatMs = $ml.benchmark.p95InferenceMs
    $trainMs = $ml.benchmark.trainTimeMs
    $modelBytes = $ml.benchmark.modelSizeBytes
    $modelKb = $ml.benchmark.modelSizeKb

    $testBuildStr = if ($testBuildOk) { "PASS" } else { "FAIL" }
    $testRunStr   = if ($testRunOk -and $failedTests -eq 0) { "PASS" } else { "FAIL" }
    $gCatchStr    = if ($guardEmptyCatch) { "PASS" } else { "FAIL" }
    $gSyncStr     = if ($guardNoSyncBlock) { "PASS" } else { "FAIL" }
    $gManStr      = if ($guardManifestPerms) { "PASS" } else { "FAIL" }
    $gSmsStr      = if ($guardNoReadSms) { "PASS" } else { "FAIL" }
    $gGitStr      = if ($guardNoKeystoreTracked) { "PASS" } else { "FAIL" }
    $gVaultStr    = if ($guardNoVaultKeyFile) { "PASS" } else { "FAIL" }
    $permsJoined  = $permissions -join ", "

    $statusLines = @(
        "# Etat Genere du Depot - SmartBudget ML",
        "",
        "> Fichier genere automatiquement par ``./tools/verify.ps1``. Ne pas modifier a la main.",
        "",
        "## 1. Metadonnees et Compilation",
        "| Cle | Valeur |",
        "|---|---|",
        "| **Date de verification** | ``$dateUtc`` |",
        "| **Branche / Commit** | ``$branch`` (``$commitShort`` / ``$commitFull``) |",
        "| **Build Tests (net10.0)** | ``$testBuildStr`` |",
        "| **Build Android (net10.0-android Release)** | ``$androidStatus`` |",
        "| **Cles de traduction (AppStrings)** | ``$frKeyCount`` FR / ``$enKeyCount`` EN |",
        "",
        "## 2. Suite de Tests xUnit",
        "| Total | Reussis | Echecs | Ignores | Statut |",
        "|---:|---:|---:|---:|---|",
        "| **$totalTests** | **$passedTests** | **$failedTests** | **$skippedTests** | ``$testRunStr`` |",
        "",
        "## 3. Garde-fous d'Audit",
        "| Garde-fou | Resultat | Detail |",
        "|---|---|---|",
        "| Aucun ``catch {}`` vide | ``$gCatchStr`` | $emptyCatchCount occurrence(s) |",
        "| Aucun ``GetAwaiter().GetResult()`` | ``$gSyncStr`` | $syncBlockCount occurrence(s) |",
        "| Aucun repli de cle en clair ``sb_vault_key`` dans ``SmartBudgetML/`` | ``$gVaultStr`` | $vaultKeyFileCount occurrence(s) |",
        "| Manifeste Android : ``USE_BIOMETRIC`` seul | ``$gManStr`` | Permissions : ``$permsJoined`` |",
        "| Aucun ``READ_SMS`` / ``RECEIVE_SMS`` | ``$gSmsStr`` | $($readSmsCount + $manifestReadSms) occurrence(s) |",
        "| ``git ls-files`` sans keystore ni ``.sb_vault_key`` | ``$gGitStr`` | $($leakedSecrets.Count) fichier(s) sensible(s) suivi(s) |",
        "| Build Release Android sans code ``SB_SELFTEST`` | ``$selfTestAbsentStr`` | ``tools/check_selftest_absent.py`` sur la DLL IL |",
        "| Bibliotheques natives alignees sur 16 Ko | ``$align16kStr`` | ``tools/check_elf_alignment.py`` sur l'APK |",
        "",
        "## 4. Metriques ML Reelles (issues de ``artifacts/ml-eval.json``)",
        "- **Echantillons :** ``$trainSamples`` entrainement (``seed_fr.csv``) / ``$holdoutSamples`` validation (``holdout_fr.csv``), ``$catCount`` categories dans le jeu courant.",
        "- **Latence d'inference (1 000 appels) :** Moyenne = ``$avgLatMs ms``, p95 = ``$p95LatMs ms``.",
        "- **Temps d'entrainement :** ``$trainMs ms``.",
        "- **Taille du modele serialise :** ``$modelBytes octets`` (``$modelKb Ko``).",
        "",
        "| Approche | Reussites / Total | Micro-Accuracy | Macro-Accuracy | Macro-F1 |",
        "|---|---:|---:|---:|---:|",
        "| **Regles seules (KeywordRuleCategorizer)** | $($ml.rulesOnly.correctCount) / $($ml.rulesOnly.totalCount) | $rulesAccPct | $rulesAccPct | $rulesF1Pct |",
        "| **ML.NET SDCA seul** | $($ml.mlOnly.correctCount) / $($ml.mlOnly.totalCount) | $mlAccPct | $mlAccPct | $mlF1Pct |",
        "| **Pipeline Hybride (Memoire + Regles + ML)** | $($ml.hybrid.correctCount) / $($ml.hybrid.totalCount) | **$hybAccPct** | **$hybAccPct** | **$hybF1Pct** |"
    )
    [System.IO.File]::WriteAllLines($statusMdPath, $statusLines, [System.Text.UTF8Encoding]::new($false))

    # 5. Concise summary (max 25 lines)
    Write-Output "=== SMARTBUDGET ML VERIFY ($commitShort on $branch) ==="
    Write-Output "Tests         : $passedTests/$totalTests passed, $failedTests failed, $skippedTests skipped"
    Write-Output "Android Build : $androidStatus"
    Write-Output "Guard Catch   : $gCatchStr ($emptyCatchCount)"
    Write-Output "Guard Async   : $gSyncStr ($syncBlockCount)"
    Write-Output "Guard Manifest: $gManStr ($permsJoined)"
    Write-Output "Guard NoSMS   : $gSmsStr"
    Write-Output "Guard GitFiles: $gGitStr"
    Write-Output "ML Hybrid     : $($ml.hybrid.correctCount)/$($ml.hybrid.totalCount) ($hybAccPct acc, $hybF1Pct F1, avg $($ml.benchmark.avgInferenceMs)ms, $($ml.benchmark.modelSizeKb)KB)"
    Write-Output "Status File   : docs/generated/STATUS.md updated"

    if ($failures.Count -gt 0) {
        Write-Output "RESULT        : FAIL ($($failures -join '; '))"
        exit 1
    }
    Write-Output "RESULT        : PASS"
    exit 0
}
finally {
    Pop-Location
}
