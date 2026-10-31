using Xunit;
namespace SpazaSnap.Tests;
public class AccountRulesTests
{
    [Theory]
    [InlineData("  SHOP@EXAMPLE.COM  ","shop@example.com")]
    [InlineData("owner+stock@example.co.za","owner+stock@example.co.za")]
    public void NormalizesEmails(string input,string expected)=>Assert.Equal(expected,AccountRules.Email(input));
    [Theory]
    [InlineData("")]
    [InlineData("not an email")]
    [InlineData("Owner <owner@example.com>")]
    public void RejectsInvalidOrDisplayNameEmails(string input)=>Assert.Throws<ArgumentException>(()=>AccountRules.Email(input));
    [Theory]
    [InlineData("short1")]
    [InlineData("allletterspassword")]
    [InlineData("1234567890123456")]
    public void RejectsWeakPasswords(string input)=>Assert.Throws<ArgumentException>(()=>AccountRules.Password(input));
    [Fact] public void AcceptsLongPassphraseWithNumber()=>AccountRules.Password("My shop has 5 bright shelves!");
    [Fact] public void RejectsOversizedPassword()=>Assert.Throws<ArgumentException>(()=>AccountRules.Password(new string('a',128)+"1"));
}
