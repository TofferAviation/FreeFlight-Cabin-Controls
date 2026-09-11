namespace FreeFlight.CabinControl.Core.Passengers;

public static class CabinServiceCatalog
{
    public static IReadOnlyList<CabinMenuItem> Menu { get; } =
    [
        new("meal-chicken", "Roast chicken supper", "Hot meal", 9.95m, "Chicken, seasonal vegetables and potato"),
        new("meal-pasta", "Tomato basil pasta", "Hot meal", 8.95m, "Vegetarian pasta with tomato and basil"),
        new("meal-breakfast", "English breakfast", "Hot meal", 8.50m, "Egg, potato, mushroom and tomato"),
        new("food-sandwich", "Club sandwich", "Light meal", 5.75m, "Chicken, salad and mayonnaise"),
        new("food-snack", "Crisps and snack box", "Snack", 3.25m, "Savoury snack selection"),
        new("drink-water", "Still water", "Soft drink", 2.25m, "500 ml bottle"),
        new("drink-soft", "Soft drink", "Soft drink", 2.75m, "Cola, lemonade or tonic"),
        new("drink-juice", "Orange juice", "Soft drink", 2.95m, "Chilled orange juice"),
        new("drink-wine", "Miniature wine", "Alcohol", 6.50m, "Red, white or sparkling wine"),
        new("drink-champagne", "Champagne", "Alcohol", 9.50m, "Single-serve sparkling wine")
    ];

    public static CabinMenuItem SelectMeal(int seed) =>
        Menu.Where(item => item.Category is "Hot meal" or "Light meal" or "Snack").ElementAt(Math.Abs(seed) % 5);

    public static CabinMenuItem SelectDrink(int seed) =>
        Menu.Where(item => item.Category is "Soft drink" or "Alcohol").ElementAt(Math.Abs(seed) % 5);
}
