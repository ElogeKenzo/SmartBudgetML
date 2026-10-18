using SmartBudgetML.Services.ML;
using Xunit;

namespace SmartBudgetML.Tests.ML;

public class ExpenseCategorizerServiceTests
{
    private readonly ExpenseCategorizerService _categorizer;

    public ExpenseCategorizerServiceTests()
    {
        _categorizer = new ExpenseCategorizerService(seed: 42);
    }

    [Fact]
    public async Task TrainAsync_ShouldTrainModelSuccessfully()
    {
        // Act
        await _categorizer.TrainAsync();

        // Assert
        Assert.True(_categorizer.IsTrained);
    }

    [Theory]
    [InlineData("Starbucks Caramel Latte", "Food & Dining")]
    [InlineData("McDonalds Double Cheeseburger", "Food & Dining")]
    [InlineData("Whole Foods organic milk and bread", "Groceries")]
    [InlineData("Uber trip to downtown office", "Transportation")]
    [InlineData("Monthly apartment rent payment", "Housing & Rent")]
    [InlineData("Electric utility bill payment", "Utilities")]
    [InlineData("Netflix monthly video subscription", "Entertainment")]
    [InlineData("Company monthly direct deposit payroll salary", "Salary / Income")]
    public async Task Predict_ShouldCategorizeKnownTransactionsAccurately(string description, string expectedCategory)
    {
        // Arrange
        await _categorizer.EnsureTrainedAsync();

        // Act
        var prediction = _categorizer.Predict(description);

        // Assert
        Assert.NotNull(prediction);
        Assert.Equal(expectedCategory, prediction.PredictedCategory);
        Assert.True(prediction.Confidence > 0f, "Confidence score should be strictly positive.");
        Assert.True(prediction.Confidence <= 1.0f, "Confidence score cannot exceed 1.0.");
    }

    [Fact]
    public async Task Predict_ShouldHandleEmptyOrWhitespaceInputGracefully()
    {
        // Arrange
        await _categorizer.EnsureTrainedAsync();

        // Act
        var prediction = _categorizer.Predict("");

        // Assert
        Assert.NotNull(prediction);
        Assert.Equal("Other", prediction.PredictedCategory);
    }

    [Fact]
    public async Task RetrainWithFeedbackAsync_ShouldIncorporateUserFeedback()
    {
        // Arrange
        await _categorizer.EnsureTrainedAsync();
        var customDescription = "Acme Corp Special Gadget";
        var userConfirmedCategory = "Shopping";

        // Act
        await _categorizer.RetrainWithFeedbackAsync(customDescription, userConfirmedCategory);
        var prediction = _categorizer.Predict(customDescription);

        // Assert
        Assert.NotNull(prediction);
        Assert.Equal(userConfirmedCategory, prediction.PredictedCategory);
    }
}
