using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using Shiny.Maui.Controls.Analysis.Ai;
using Shouldly;
using Xunit;

namespace Shiny.Maui.Controls.Camera.Tests;

public class FoundationModelsChatClientTests
{
    static readonly FoundationModelsChatClientOptions Defaults = new();
    static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xD9];

    record LineItem(string Name, int Quantity, double Amount);
    record Receipt(string Merchant, string? InvoiceNumber, double Total, List<LineItem> Lines, LineItem? MostExpensive);
    record TreeNode(string Name, List<TreeNode> Children);

    static JsonElement SchemaOf<T>() => AIJsonUtilities.CreateJsonSchema(typeof(T));

    // every node of a rewritten schema, so a test can assert nothing anywhere still uses an unsupported form
    static IEnumerable<JsonObject> Nodes(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            yield return obj;
            foreach (var child in obj.Select(p => p.Value).SelectMany(Nodes))
                yield return child;
        }
        else if (node is JsonArray array)
        {
            foreach (var child in array.SelectMany(Nodes))
                yield return child;
        }
    }


    [Fact]
    public void System_messages_and_options_become_instructions()
    {
        var request = FoundationModelsRequestBuilder.Build(
            [new(ChatRole.System, "Be terse."), new(ChatRole.User, "Hi")],
            new ChatOptions { Instructions = "Answer in English." },
            Defaults,
            false);

        request.Payload.Instructions.ShouldBe("Answer in English.\n\nBe terse.");
        request.Payload.Prompt.Text.ShouldBe("Hi");
        request.Payload.History.ShouldBeNull();
    }

    [Fact]
    public void Last_user_message_is_the_prompt_and_earlier_turns_are_history()
    {
        var request = FoundationModelsRequestBuilder.Build(
            [
                new(ChatRole.User, "My colour is teal."),
                new(ChatRole.Assistant, "Noted."),
                new(ChatRole.User, "What is my colour?"),
                new(ChatRole.Assistant, "a stale reply with nowhere to go")
            ],
            null,
            Defaults,
            false);

        request.Payload.Prompt.Text.ShouldBe("What is my colour?");
        request.Payload.History!.Select(t => (t.Role, t.Text)).ShouldBe([("user", "My colour is teal."), ("assistant", "Noted.")]);
    }

    [Fact]
    public void Images_are_indexed_across_turns()
    {
        var request = FoundationModelsRequestBuilder.Build(
            [
                new(ChatRole.User, [new TextContent("first"), new DataContent(Jpeg, "image/jpeg")]),
                new(ChatRole.Assistant, "ok"),
                new(ChatRole.User, [new TextContent("second"), new DataContent(Jpeg, "image/png"), new DataContent(Jpeg, "image/jpeg")])
            ],
            null,
            Defaults,
            false);

        request.Images.Count.ShouldBe(3);
        request.Payload.History![0].Images.ShouldBe([0]);
        request.Payload.Prompt.Images.ShouldBe([1, 2]);
    }

    [Fact]
    public void Non_image_data_is_ignored()
    {
        var request = FoundationModelsRequestBuilder.Build(
            [new(ChatRole.User, [new TextContent("read this"), new DataContent(Jpeg, "application/pdf")])],
            null,
            Defaults,
            false);

        request.Images.ShouldBeEmpty();
        request.Payload.Prompt.Images.ShouldBeNull();
    }

    [Fact]
    public void Remote_images_are_rejected()
    {
        Should.Throw<NotSupportedException>(() => FoundationModelsRequestBuilder.Build(
            [new(ChatRole.User, [new UriContent(new Uri("https://example.com/a.jpg"), "image/jpeg")])],
            null,
            Defaults,
            false));
    }

    [Fact]
    public void AIFunction_tools_are_rejected()
    {
        var options = new ChatOptions { Tools = [AIFunctionFactory.Create(() => 42, "answer")] };
        Should.Throw<NotSupportedException>(() => FoundationModelsRequestBuilder.Build([new(ChatRole.User, "hi")], options, Defaults, false));
    }

    [Fact]
    public void A_user_message_is_required()
    {
        Should.Throw<ArgumentException>(() => FoundationModelsRequestBuilder.Build([new(ChatRole.System, "Be terse.")], null, Defaults, false));
    }

    [Fact]
    public void Options_map_onto_the_payload()
    {
        var request = FoundationModelsRequestBuilder.Build(
            [new(ChatRole.User, "hi")],
            new ChatOptions { Temperature = 0.2f, MaxOutputTokens = 300 },
            new FoundationModelsChatClientOptions { UseOcrTool = true, UseBarcodeReaderTool = true, UseCase = FoundationModelsUseCase.ContentTagging },
            true);

        request.Payload.Temperature!.Value.ShouldBe(0.2, 0.0001);
        request.Payload.MaxTokens.ShouldBe(300);
        request.Payload.Tools.ShouldBe(["ocr", "barcode"]);
        request.Payload.UseCase.ShouldBe("contentTagging");
        request.Payload.Stream.ShouldBeTrue();
    }

    [Fact]
    public void A_json_schema_turns_on_guided_generation_and_streaming_off()
    {
        var request = FoundationModelsRequestBuilder.Build(
            [new(ChatRole.User, "extract")],
            new ChatOptions { ResponseFormat = ChatResponseFormat.ForJsonSchema(SchemaOf<Receipt>()) },
            Defaults,
            true);

        request.Payload.Schema.ShouldNotBeNull();
        request.JsonInPrompt.ShouldBeFalse();
        request.Payload.Stream.ShouldBeFalse(); // partial JSON is not an append-only text stream
        request.Payload.Instructions.ShouldBeNull();
    }

    [Fact]
    public void Json_without_a_schema_is_asked_for_in_the_instructions()
    {
        var request = FoundationModelsRequestBuilder.Build(
            [new(ChatRole.User, "list three colours")],
            new ChatOptions { ResponseFormat = ChatResponseFormat.Json },
            Defaults,
            false);

        request.Payload.Schema.ShouldBeNull();
        request.JsonInPrompt.ShouldBeTrue();
        request.Payload.Instructions!.ShouldContain("JSON");
    }

    [Fact]
    public void A_schema_it_cannot_express_falls_back_to_the_instructions()
    {
        var request = FoundationModelsRequestBuilder.Build(
            [new(ChatRole.User, "build a tree")],
            new ChatOptions { ResponseFormat = ChatResponseFormat.ForJsonSchema(SchemaOf<TreeNode>()) },
            Defaults,
            false);

        request.Payload.Schema.ShouldBeNull();
        request.JsonInPrompt.ShouldBeTrue();
        request.Payload.Instructions!.ShouldContain("\"children\"");
    }

    [Fact]
    public void Payload_json_is_camel_case_and_omits_nulls()
    {
        var json = FoundationModelsRequestBuilder.Build([new(ChatRole.User, "hi")], null, Defaults, false).ToJson();

        json.ShouldBe("""{"prompt":{"role":"user","text":"hi"},"stream":false}""");
    }


    [Fact]
    public void Nullable_types_collapse_and_leave_required()
    {
        var rewritten = FoundationModelsSchema.ToAppleSchema(SchemaOf<Receipt>());

        var root = JsonNode.Parse(rewritten!)!.AsObject();
        Nodes(root).ShouldAllBe(n => !(n["type"] is JsonArray));
        root["properties"]!["invoiceNumber"]!["type"]!.GetValue<string>().ShouldBe("string");
        root["required"]!.AsArray().Select(r => r!.GetValue<string>()).ShouldBe(["merchant", "total", "lines"], ignoreOrder: true);
    }

    [Fact]
    public void Pointer_references_are_inlined()
    {
        // a repeated type written as a JSON pointer to its first occurrence, which Foundation Models cannot resolve
        using var doc = JsonDocument.Parse("""
            {"type":"object","properties":{
              "lines":{"type":"array","items":{"type":"object","properties":{"name":{"type":"string"},"amount":{"type":"number"}},"required":["name","amount"]}},
              "mostExpensive":{"description":"the priciest line","$ref":"#/properties/lines/items"}
            },"required":["lines","mostExpensive"]}
            """);

        var root = JsonNode.Parse(FoundationModelsSchema.ToAppleSchema(doc.RootElement)!)!.AsObject();

        Nodes(root).ShouldAllBe(n => !n.ContainsKey("$ref"));
        var mostExpensive = root["properties"]!["mostExpensive"]!.AsObject();
        mostExpensive["type"]!.GetValue<string>().ShouldBe("object");
        mostExpensive["description"]!.GetValue<string>().ShouldBe("the priciest line"); // keywords beside the $ref survive
        mostExpensive["properties"]!.AsObject().Select(p => p.Key).ShouldBe(["name", "amount"], ignoreOrder: true);
    }

    [Fact]
    public void A_repeated_type_from_MEAI_survives_the_rewrite()
    {
        var root = JsonNode.Parse(FoundationModelsSchema.ToAppleSchema(SchemaOf<Receipt>())!)!.AsObject();

        var mostExpensive = root["properties"]!["mostExpensive"]!.AsObject();
        mostExpensive["type"]!.GetValue<string>().ShouldBe("object");
        mostExpensive["properties"]!.AsObject().Select(p => p.Key).ShouldBe(["name", "quantity", "amount"], ignoreOrder: true);
    }

    [Fact]
    public void AnyOf_with_null_collapses_to_the_other_branch()
    {
        using var doc = JsonDocument.Parse("""
            {"type":"object","properties":{"a":{"description":"kept","anyOf":[{"type":"string"},{"type":"null"}]}},"required":["a"]}
            """);

        var root = JsonNode.Parse(FoundationModelsSchema.ToAppleSchema(doc.RootElement)!)!.AsObject();

        var a = root["properties"]!["a"]!.AsObject();
        a.ContainsKey("anyOf").ShouldBeFalse();
        a["type"]!.GetValue<string>().ShouldBe("string");
        a["description"]!.GetValue<string>().ShouldBe("kept");
        root["required"]!.AsArray().Count.ShouldBe(0);
    }

    [Fact]
    public void Defs_references_are_inlined_and_defs_removed()
    {
        using var doc = JsonDocument.Parse("""
            {"$schema":"https://json-schema.org/draft/2020-12/schema","type":"object","properties":{"a":{"$ref":"#/$defs/X"}},"required":["a"],"$defs":{"X":{"type":"object","properties":{"b":{"type":"string"}},"required":["b"]}}}
            """);

        var root = JsonNode.Parse(FoundationModelsSchema.ToAppleSchema(doc.RootElement)!)!.AsObject();

        root.ContainsKey("$defs").ShouldBeFalse();
        root.ContainsKey("$schema").ShouldBeFalse();
        root["properties"]!["a"]!["properties"]!["b"]!["type"]!.GetValue<string>().ShouldBe("string");
    }

    [Fact]
    public void Real_unions_and_recursive_types_are_not_expressible()
    {
        using var union = JsonDocument.Parse("""{"type":"object","properties":{"a":{"type":["string","number"]}}}""");

        FoundationModelsSchema.ToAppleSchema(union.RootElement).ShouldBeNull();
        FoundationModelsSchema.ToAppleSchema(SchemaOf<TreeNode>()).ShouldBeNull();
    }


    [Theory]
    [InlineData("```json\n{\"a\":1}\n```", "{\"a\":1}")]
    [InlineData("```\n[1,2]\n```\n", "[1,2]")]
    [InlineData("{\"a\":1}", "{\"a\":1}")]
    [InlineData("```not closed", "```not closed")]
    public void Code_fences_are_stripped(string text, string expected)
        => FoundationModelsRequestBuilder.StripCodeFence(text).ShouldBe(expected);


    [Fact]
    public async Task Off_Apple_it_reports_unsupported_and_throws()
    {
        FoundationModelsChatClient.Availability.ShouldBe(FoundationModelsAvailability.UnsupportedPlatform);
        FoundationModelsChatClient.IsAvailable.ShouldBeFalse();
        FoundationModelsChatClient.Prewarm(); // no-op, must not throw

        IChatClient client = new FoundationModelsChatClient();
        client.GetService<ChatClientMetadata>()!.DefaultModelId.ShouldBe(FoundationModelsChatClient.ModelId);
        await Should.ThrowAsync<PlatformNotSupportedException>(() => client.GetResponseAsync("hi"));
    }
}
