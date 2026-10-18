using SmartBudgetML.Services.ML.Models;

namespace SmartBudgetML.Services.ML;

/// <summary>
/// Curated local training dataset covering common expense and income transactions.
/// Enables 100% on-device cold-start training with zero cloud dependency.
/// </summary>
public static class SeedTrainingData
{
    public static List<ExpenseTransactionData> GetDefaultDataset()
    {
        return new List<ExpenseTransactionData>
        {
            // Food & Dining
            new() { Description = "Starbucks Caramel Frappuccino Coffee", CategoryName = "Food & Dining" },
            new() { Description = "McDonalds Big Mac Meal Fries", CategoryName = "Food & Dining" },
            new() { Description = "Dinner at Olive Garden Italian restaurant", CategoryName = "Food & Dining" },
            new() { Description = "Subway footlong turkey sandwich", CategoryName = "Food & Dining" },
            new() { Description = "Chipotle Mexican grill chicken burrito bowl", CategoryName = "Food & Dining" },
            new() { Description = "Local bakery croissant cappuccino", CategoryName = "Food & Dining" },
            new() { Description = "Burger King whopper meal", CategoryName = "Food & Dining" },
            new() { Description = "Pizza Hut pepperoni pan pizza delivery", CategoryName = "Food & Dining" },
            new() { Description = "Sushi express lunch combo dinner", CategoryName = "Food & Dining" },
            new() { Description = "Taco Bell taco party pack", CategoryName = "Food & Dining" },
            new() { Description = "Dunkin Donuts iced latte bagel", CategoryName = "Food & Dining" },
            new() { Description = "KFC fried chicken bucket", CategoryName = "Food & Dining" },

            // Groceries
            new() { Description = "Whole Foods organic milk eggs apples", CategoryName = "Groceries" },
            new() { Description = "Walmart grocery aisle meat cereal vegetables", CategoryName = "Groceries" },
            new() { Description = "Trader Joes almond milk frozen pizza salad", CategoryName = "Groceries" },
            new() { Description = "Carrefour supermarket weekly food supplies", CategoryName = "Groceries" },
            new() { Description = "Costco wholesale bulk groceries snacks", CategoryName = "Groceries" },
            new() { Description = "Aldi fresh vegetables fruit bread cheese", CategoryName = "Groceries" },
            new() { Description = "Target fresh produce chicken pasta sauce", CategoryName = "Groceries" },
            new() { Description = "Local butcher shop fresh beef steak chicken", CategoryName = "Groceries" },
            new() { Description = "Green grocery fresh fruits oranges bananas", CategoryName = "Groceries" },

            // Transportation
            new() { Description = "Uber ride downtown city center", CategoryName = "Transportation" },
            new() { Description = "Lyft trip to international airport", CategoryName = "Transportation" },
            new() { Description = "Shell gas station fuel unleaded petrol", CategoryName = "Transportation" },
            new() { Description = "Chevron gasoline diesel refill", CategoryName = "Transportation" },
            new() { Description = "Subway metro rail transit monthly card pass", CategoryName = "Transportation" },
            new() { Description = "Public bus fare ticket reload", CategoryName = "Transportation" },
            new() { Description = "City parking garage meter payment", CategoryName = "Transportation" },
            new() { Description = "Highway toll booth fee charge", CategoryName = "Transportation" },
            new() { Description = "Train ticket Amtrak central station", CategoryName = "Transportation" },
            new() { Description = "Car wash auto detailing service", CategoryName = "Transportation" },

            // Housing & Rent
            new() { Description = "Monthly apartment rent payment landlord", CategoryName = "Housing & Rent" },
            new() { Description = "Home mortgage loan monthly payment bank", CategoryName = "Housing & Rent" },
            new() { Description = "Condo HOA maintenance monthly fee", CategoryName = "Housing & Rent" },
            new() { Description = "Homeowner property insurance premium", CategoryName = "Housing & Rent" },
            new() { Description = "Plumber repair leaking pipe bathroom", CategoryName = "Housing & Rent" },

            // Utilities
            new() { Description = "Electric utility power company electric bill", CategoryName = "Utilities" },
            new() { Description = "City water and sewage service bill", CategoryName = "Utilities" },
            new() { Description = "Natural gas home heating winter bill", CategoryName = "Utilities" },
            new() { Description = "Comcast Xfinity broadband internet fiber wifi", CategoryName = "Utilities" },
            new() { Description = "AT&T mobile cellular phone unlimited plan", CategoryName = "Utilities" },
            new() { Description = "Verizon wireless phone bill data plan", CategoryName = "Utilities" },
            new() { Description = "Trash collection municipal waste recycling fee", CategoryName = "Utilities" },

            // Healthcare
            new() { Description = "CVS pharmacy prescription medication pills", CategoryName = "Healthcare" },
            new() { Description = "Walgreens pharmacy vitamins cold medicine", CategoryName = "Healthcare" },
            new() { Description = "Dental clinic teeth cleaning checkup x-ray", CategoryName = "Healthcare" },
            new() { Description = "Doctor medical clinic consultation copay", CategoryName = "Healthcare" },
            new() { Description = "Optometrist eye exam prescription glasses lenses", CategoryName = "Healthcare" },
            new() { Description = "Physical therapy rehabilitation session", CategoryName = "Healthcare" },
            new() { Description = "Health insurance monthly premium copayment", CategoryName = "Healthcare" },

            // Entertainment
            new() { Description = "Netflix monthly streaming subscription", CategoryName = "Entertainment" },
            new() { Description = "Spotify music premium family plan subscription", CategoryName = "Entertainment" },
            new() { Description = "AMC theater cinema movie tickets popcorn", CategoryName = "Entertainment" },
            new() { Description = "PlayStation network PSN store digital game", CategoryName = "Entertainment" },
            new() { Description = "Steam video game digital purchase PC", CategoryName = "Entertainment" },
            new() { Description = "Disney plus streaming subscription", CategoryName = "Entertainment" },
            new() { Description = "Concert live music festival tickets", CategoryName = "Entertainment" },
            new() { Description = "Bowling alley arcade games drinks", CategoryName = "Entertainment" },

            // Shopping
            new() { Description = "Amazon prime online order household items", CategoryName = "Shopping" },
            new() { Description = "Zara clothing store jacket shirt pants", CategoryName = "Shopping" },
            new() { Description = "Nike running sneakers sport shoes athletic wear", CategoryName = "Shopping" },
            new() { Description = "Best Buy electronics wireless headphones charger", CategoryName = "Shopping" },
            new() { Description = "IKEA home furniture desk chair bookshelf", CategoryName = "Shopping" },
            new() { Description = "H&M summer fashion dress t-shirt jeans", CategoryName = "Shopping" },
            new() { Description = "Apple store iPad cable phone case accessory", CategoryName = "Shopping" },

            // Salary / Income
            new() { Description = "Company payroll monthly direct deposit salary", CategoryName = "Salary / Income" },
            new() { Description = "Freelance web design client invoice payment", CategoryName = "Salary / Income" },
            new() { Description = "Software consulting contract milestone payout", CategoryName = "Salary / Income" },
            new() { Description = "Quarterly company performance employee bonus", CategoryName = "Salary / Income" },
            new() { Description = "Side business sales earnings revenue", CategoryName = "Salary / Income" },

            // Investments
            new() { Description = "Vanguard S&P 500 index ETF stock purchase", CategoryName = "Investments" },
            new() { Description = "Coinbase cryptocurrency Bitcoin Ethereum buy", CategoryName = "Investments" },
            new() { Description = "Fidelity 401k retirement fund deposit contribution", CategoryName = "Investments" },
            new() { Description = "Robinhood dividend stock share investment", CategoryName = "Investments" }
        };
    }
}
