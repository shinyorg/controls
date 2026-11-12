using System.Text.Json;
using System.Text.Json.Nodes;
using Shiny.Controls.AgentUI.Json;

namespace Shiny.Controls.AgentUI.Tests;

public class JsonPatchTests
{
    static JsonNode? Patch(string doc, string patch) => JsonPatch.Apply(JsonNode.Parse(doc), JsonElement.Parse(patch));

    static void Same(JsonNode? actual, string expected) =>
        JsonNode.DeepEquals(actual, JsonNode.Parse(expected)).ShouldBeTrue($"got {actual?.ToJsonString()}");

    // RFC 6902 appendix A
    [Theory]
    [InlineData("""{"foo":"bar"}""", """[{"op":"add","path":"/baz","value":"qux"}]""", """{"baz":"qux","foo":"bar"}""")]
    [InlineData("""{"foo":["bar","baz"]}""", """[{"op":"add","path":"/foo/1","value":"qux"}]""", """{"foo":["bar","qux","baz"]}""")]
    [InlineData("""{"baz":"qux","foo":"bar"}""", """[{"op":"remove","path":"/baz"}]""", """{"foo":"bar"}""")]
    [InlineData("""{"foo":["bar","qux","baz"]}""", """[{"op":"remove","path":"/foo/1"}]""", """{"foo":["bar","baz"]}""")]
    [InlineData("""{"baz":"qux","foo":"bar"}""", """[{"op":"replace","path":"/baz","value":"boo"}]""", """{"baz":"boo","foo":"bar"}""")]
    [InlineData("""{"foo":{"bar":"baz","waldo":"fred"},"qux":{"corge":"grault"}}""", """[{"op":"move","from":"/foo/waldo","path":"/qux/thud"}]""", """{"foo":{"bar":"baz"},"qux":{"corge":"grault","thud":"fred"}}""")]
    [InlineData("""{"foo":["all","grass","cows","eat"]}""", """[{"op":"move","from":"/foo/1","path":"/foo/3"}]""", """{"foo":["all","cows","eat","grass"]}""")]
    [InlineData("""{"foo":["bar"]}""", """[{"op":"add","path":"/foo/-","value":["abc","def"]}]""", """{"foo":["bar",["abc","def"]]}""")]
    [InlineData("""{"foo":"bar"}""", """[{"op":"copy","from":"/foo","path":"/baz"}]""", """{"foo":"bar","baz":"bar"}""")]
    [InlineData("""{"/":9,"~1":10}""", """[{"op":"test","path":"/~01","value":10}]""", """{"/":9,"~1":10}""")]
    [InlineData("""{"a":1}""", """[{"op":"replace","path":"","value":{"b":2}}]""", """{"b":2}""")]
    public void Rfc6902(string doc, string patch, string expected) => Same(Patch(doc, patch), expected);

    [Theory]
    [InlineData("""{"foo":"bar"}""", """[{"op":"add","path":"/baz/bat","value":"qux"}]""")]   // A.12: parent missing
    [InlineData("""{"baz":"qux"}""", """[{"op":"test","path":"/baz","value":"bar"}]""")]      // A.9: test fails
    [InlineData("""{"foo":"bar"}""", """[{"op":"remove","path":"/nope"}]""")]
    [InlineData("""{"foo":"bar"}""", """[{"op":"replace","path":"/nope","value":1}]""")]
    [InlineData("""{"foo":[1]}""", """[{"op":"add","path":"/foo/5","value":1}]""")]
    [InlineData("""{"foo":[1]}""", """[{"op":"remove","path":"/foo/01"}]""")]
    [InlineData("""{"foo":"bar"}""", """[{"op":"frobnicate","path":"/foo"}]""")]
    [InlineData("""{"a":{"b":1}}""", """[{"op":"move","from":"/a","path":"/a/b/c"}]""")]
    public void Failures(string doc, string patch) =>
        Should.Throw<JsonPatchException>(() => Patch(doc, patch));

    [Fact]
    public void AtomicOnFailure()
    {
        var doc = JsonNode.Parse("""{"a":1}""");
        Should.Throw<JsonPatchException>(() => JsonPatch.Apply(doc, JsonElement.Parse("""[{"op":"add","path":"/b","value":2},{"op":"remove","path":"/zzz"}]""")));
        Same(doc, """{"a":1}""");
    }

    [Fact]
    public void Pointer_SlashIsRootOnlyWhenAsked()
    {
        JsonPointer.Parse("/", slashIsRoot: true).ShouldBeEmpty();
        JsonPointer.Parse("/").ShouldBe([""]);
        JsonPointer.Parse("/a~1b/c~0d").ShouldBe(["a/b", "c~d"]);
        Should.Throw<FormatException>(() => JsonPointer.Parse("nope"));
    }

    [Fact]
    public void Pointer_UpsertCreatesParentsAndNullRemoves()
    {
        var root = JsonPointer.Upsert(null, JsonPointer.Parse("/user/name"), JsonValue.Create("Allan"));
        Same(root, """{"user":{"name":"Allan"}}""");

        root = JsonPointer.Upsert(root, JsonPointer.Parse("/user/name"), null);
        Same(root, """{"user":{}}""");

        root = JsonPointer.Upsert(root, JsonPointer.Parse("/list/-"), JsonValue.Create(1));
        Same(root, """{"user":{},"list":{"-":1}}""");
    }

    [Theory]
    [InlineData("/a/b", "/a", true)]
    [InlineData("/a", "/a/b", true)]
    [InlineData("/a", "/a", true)]
    [InlineData("/ab", "/a", false)]
    [InlineData("/a/b", "/a/c", false)]
    [InlineData("/x", "/", true)]
    [InlineData("/x", "", true)]
    public void Pointer_Overlaps(string read, string changed, bool expected) =>
        JsonPointer.Overlaps(read, changed).ShouldBe(expected);
}
