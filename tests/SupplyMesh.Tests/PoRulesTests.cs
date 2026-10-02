using NUnit.Framework;
using SupplyMesh.Orders.Api;
namespace SupplyMesh.Tests;

// Written test-first (TDD): red -> green -> refactor. See docs/TDD.md
[TestFixture]
public class PoRulesTests
{
    [TestCase("CREATED", "APPROVED", true)]
    [TestCase("CREATED", "SHIPPED", false)]
    [TestCase("APPROVED", "SHIPPED", true)]
    [TestCase("SHIPPED", "RECEIVED", true)]
    [TestCase("RECEIVED", "CANCELLED", false)]
    [TestCase("CANCELLED", "APPROVED", false)]
    [TestCase("BOGUS", "APPROVED", false)]
    public void CanTransition_follows_state_machine(string from, string to, bool expected) =>
        Assert.That(PoRules.CanTransition(from, to), Is.EqualTo(expected));

    [Test] public void ValidateCreate_rejects_empty_lines() =>
        Assert.That(PoRules.ValidateCreate(new(1, [])), Does.Contain("at least one"));

    [Test] public void ValidateCreate_rejects_non_positive_qty() =>
        Assert.That(PoRules.ValidateCreate(new(1, [new(10, 0)])), Does.Contain("positive"));

    [Test] public void ValidateCreate_rejects_duplicate_items() =>
        Assert.That(PoRules.ValidateCreate(new(1, [new(10, 1), new(10, 2)])), Does.Contain("Duplicate"));

    [Test] public void ValidateCreate_accepts_valid_request() =>
        Assert.That(PoRules.ValidateCreate(new(1, [new(10, 1), new(11, 5)])), Is.Empty);

    [Test] public void NewPoNumber_has_expected_format_and_is_unique()
    {
        var a = PoRules.NewPoNumber(new DateTime(2026, 10, 2));
        var b = PoRules.NewPoNumber(new DateTime(2026, 10, 2));
        Assert.That(a, Does.Match(@"^PO-20261002-[0-9A-F]{6}$"));
        Assert.That(a, Is.Not.EqualTo(b));
    }
}
