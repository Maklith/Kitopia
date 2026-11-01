using System.Globalization;
using Math = Kitopia.Desktop.Features.Search.Utilities.Math;

namespace KitopiaTest.Search;

[TestClass]
public sealed class MathTests
{
    private CultureInfo _previousCulture = null!;

    [TestInitialize]
    public void Initialize()
    {
        _previousCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
    }

    [TestCleanup]
    public void Cleanup() => CultureInfo.CurrentCulture = _previousCulture;

    [TestMethod]
    [DataRow("3^34", "16677181699666569")]
    [DataRow("9007199254740993^1", "9007199254740993")]
    [DataRow("79228162514264337593543950335^1", "79228162514264337593543950335")]
    [DataRow("1.0000000000000000000000000001^2", "1.0000000000000000000000000002")]
    [DataRow("0.1^28", "0.0000000000000000000000000001")]
    [DataRow("2^-3", "0.125")]
    [DataRow("10^-28", "0.0000000000000000000000000001")]
    [DataRow("(-2)^3", "-8")]
    [DataRow("(-2)^4", "16")]
    [DataRow("(-2)^-3", "-0.125")]
    [DataRow("0^0", "1")]
    [DataRow("0^3", "0")]
    [DataRow("2^0", "1")]
    [DataRow("2^3.0", "8")]
    [DataRow("1^79228162514264337593543950335", "1")]
    [DataRow("(-1)^79228162514264337593543950335", "-1")]
    [DataRow("2*3^4+1", "163")]
    public void Evaluate_IntegerPower_PreservesDecimalPrecision(string expression, string expected)
    {
        Assert.AreEqual(decimal.Parse(expected, CultureInfo.InvariantCulture), Math.Evaluate(expression));
    }

    [TestMethod]
    [DataRow("9^0.5", "3")]
    [DataRow("4^-0.5", "0.5")]
    public void Evaluate_FractionalPower_PreservesExistingApproximation(string expression, string expected)
    {
        Assert.AreEqual(decimal.Parse(expected, CultureInfo.InvariantCulture), Math.Evaluate(expression));
    }

    [TestMethod]
    public void Evaluate_IntegerPowerOutsideDecimalRange_ThrowsOverflow()
    {
        Assert.ThrowsExactly<OverflowException>(() => Math.Evaluate("10^29"));
    }

    [TestMethod]
    public void Evaluate_ZeroToNegativePower_ThrowsDivideByZero()
    {
        Assert.ThrowsExactly<DivideByZeroException>(() => Math.Evaluate("0^-1"));
    }
}
