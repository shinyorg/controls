using Shiny.Controls.AgentUI.A2UI;

namespace Shiny.Controls.AgentUI.Tests.A2UI;

public class A2UIRegexTests
{
    // the spec's own examples
    [Theory]
    [InlineData(@"^[a-z]+@[a-z]+\.com$")]
    [InlineData(@"^(?=.*\d).{8,}$")]
    [InlineData(@"\w+\s+\w+")]
    [InlineData(@"^\d{3}-\d{4}$")]
    [InlineData(@"(ab|cd)*")]
    [InlineData(@"[\u{1F600}-\u{1F64F}]")]
    [InlineData(@"a{1,1000}")]
    public void Accepted(string pattern) => Should.NotThrow(() => A2UIRegex.Compile(pattern));

    [Theory]
    [InlineData(@"(a+)+$")]          // rule 2
    [InlineData(@"(a|ab)*c")]        // rule 3
    [InlineData(@"\d+\s*\d+")]       // rule 4
    [InlineData(@"(\w)\1")]          // backreference
    [InlineData(@"(?<=a)b")]         // lookbehind
    [InlineData(@"(?<n>a)")]         // named group
    [InlineData(@"\p{L}")]           // property escape
    [InlineData(@"(?i)abc")]         // inline flags
    [InlineData(@"\cA")]
    [InlineData(@"\01")]             // legacy octal
    [InlineData(@"\q")]              // identity escape of a non-syntax char
    [InlineData(@"a{")]              // lone brace (Unicode mode)
    [InlineData(@"a**")]
    [InlineData(@"[z-a]")]
    [InlineData(@"^*")]
    [InlineData(@"(abc")]
    public void Rejected(string pattern) =>
        Should.Throw<A2UIExpressionException>(() => A2UIRegex.Compile(pattern)).Code.ShouldBe(A2UIErrorCodes.ExpressionError);

    [Fact]
    public void Rejected_TooLong() =>
        Should.Throw<A2UIExpressionException>(() => A2UIRegex.Compile(new string('a', 257)));

    [Theory]
    [InlineData(@"^abc$", "abc\n", false)]       // $ never matches before a trailing newline
    [InlineData(@"^\d+$", "١٢٣", false)]          // \d is ASCII-only
    [InlineData(@"^\w+$", "café", false)]         // so is \w
    [InlineData(@"^.$", "😀", true)]             // . takes an astral code point whole
    [InlineData(@"^[😀-😎]$", "😃", true)]
    [InlineData(@"^\s$", " ", true)]         // ECMAScript \s
    [InlineData(@"b", "abc", true)]               // search semantics
    [InlineData(@"^a.c$", "a\nc", false)]         // . excludes line terminators
    [InlineData(@"\bcat\b", "a cat sat", true)]
    [InlineData(@"^ABC$", "abc", false)]          // case-sensitive
    public void JavaScriptSemantics(string pattern, string value, bool matches) =>
        A2UIRegex.Test(value, pattern).Valid.ShouldBe(matches);

    [Fact]
    public void OverlongValuesDontMatch() =>
        A2UIRegex.Test(new string('a', 4097), "a").Valid.ShouldBeFalse();
}
