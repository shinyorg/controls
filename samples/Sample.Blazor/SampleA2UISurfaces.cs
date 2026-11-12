namespace Sample.Blazor;

/// <summary>
/// A2UI fixtures for the gallery page that the scripted agent doesn't send: a kitchen sink that uses
/// every basic-catalog component and a few of its functions.
/// </summary>
public static class SampleA2UISurfaces
{
    /// <summary>Every basic component: text, media, layout, tabs, a modal, every input with checks, and a button that opens a URL.</summary>
    public const string KitchenSinkJson = """
        [
          {
            "version": "1.0",
            "createSurface": {
              "surfaceId": "kitchen-sink",
              "catalogId": "https://a2ui.org/specification/v1_0/catalogs/basic/catalog.json",
              "dataModel": {
                "user": { "name": "Ada", "email": "", "bio": "", "pin": "", "age": "36" },
                "price": 1234.5,
                "items": [
                  { "title": "Flat white", "qty": 2 },
                  { "title": "Croissant", "qty": 1 },
                  { "title": "Orange juice", "qty": 3 }
                ],
                "agree": false,
                "size": ["m"],
                "toppings": ["cheese"],
                "volume": 40,
                "when": { "date": "2026-11-20", "time": "09:30", "both": "2026-11-20T18:00" }
              },
              "components": [
                { "id": "root", "component": "Card", "child": "page" },
                { "id": "page", "component": "Column", "children": ["title", "intro", "tabs"] },
                { "id": "title", "component": "Text", "variant": "h2", "text": "Kitchen sink" },
                { "id": "intro", "component": "Text", "variant": "caption",
                  "text": { "@call": "formatString", "args": { "value": "Hello ${/user/name} - every *basic* component in one surface, in your locale: ${formatCurrency(value: ${/price}, currency: 'EUR')}." } } },

                { "id": "tabs", "component": "Tabs", "tabs": [
                  { "title": "Display", "child": "displayTab" },
                  { "title": "Layout", "child": "layoutTab" },
                  { "title": "Inputs", "child": "inputsTab" },
                  { "title": "Media", "child": "mediaTab" }
                ] },

                { "id": "displayTab", "component": "Column", "children": ["h1", "h3", "body", "caption", "iconRow", "divider1", "image"] },
                { "id": "h1", "component": "Text", "variant": "h1", "text": "Heading 1" },
                { "id": "h3", "component": "Text", "variant": "h3", "text": "Heading 3" },
                { "id": "body", "component": "Text", "text": "Body text with **bold**, *italic* and `code`." },
                { "id": "caption", "component": "Text", "variant": "caption", "text": "A caption, de-emphasised." },
                { "id": "iconRow", "component": "Row", "align": "center", "children": ["i1", "i2", "i3", "i4", "i5", "i6"] },
                { "id": "i1", "component": "Icon", "name": "home", "@accessibility": { "label": "Home" } },
                { "id": "i2", "component": "Icon", "name": "favorite" },
                { "id": "i3", "component": "Icon", "name": "settings" },
                { "id": "i4", "component": "Icon", "name": "search" },
                { "id": "i5", "component": "Icon", "name": "star" },
                { "id": "i6", "component": "Icon", "name": { "svgPath": "M12 2 L22 22 L2 22 Z" }, "@accessibility": { "label": "Custom triangle" } },
                { "id": "divider1", "component": "Divider" },
                { "id": "image", "component": "Image", "variant": "mediumFeature", "fit": "cover", "description": "A gradient placeholder",
                  "url": "data:image/svg+xml;utf8,<svg xmlns='http://www.w3.org/2000/svg' width='280' height='140'><defs><linearGradient id='g'><stop offset='0' stop-color='%230055D9'/><stop offset='1' stop-color='%236A4CAD'/></linearGradient></defs><rect width='280' height='140' fill='url(%23g)'/></svg>" },

                { "id": "layoutTab", "component": "Column", "children": ["weighted", "spread", "listTitle", "list", "hlist", "modal"] },
                { "id": "weighted", "component": "Row", "children": ["w1", "vdiv", "w2"] },
                { "id": "w1", "component": "Text", "weight": 2, "text": "weight 2 - takes two thirds" },
                { "id": "vdiv", "component": "Divider", "axis": "vertical" },
                { "id": "w2", "component": "Text", "weight": 1, "text": "weight 1" },
                { "id": "spread", "component": "Row", "justify": "spaceBetween", "children": ["left", "right"] },
                { "id": "left", "component": "Text", "text": "spaceBetween: left" },
                { "id": "right", "component": "Text", "text": "right" },
                { "id": "listTitle", "component": "Text", "variant": "h4", "text": { "@call": "pluralize", "args": { "value": 3, "one": "1 item in your order", "other": "3 items in your order" } } },
                { "id": "list", "component": "List", "children": { "componentId": "orderLine", "path": "/items" } },
                { "id": "orderLine", "component": "Row", "justify": "spaceBetween", "children": ["lineTitle", "lineQty"] },
                { "id": "lineTitle", "component": "Text", "text": { "@path": "title" } },
                { "id": "lineQty", "component": "Text", "variant": "caption", "text": { "@call": "formatString", "args": { "value": "#${@index(offset: 1)} - qty ${qty}" } } },
                { "id": "hlist", "component": "List", "direction": "horizontal", "children": ["c1", "c2", "c3", "c4"] },
                { "id": "c1", "component": "Card", "child": "c1t" }, { "id": "c1t", "component": "Text", "text": "Horizontal" },
                { "id": "c2", "component": "Card", "child": "c2t" }, { "id": "c2t", "component": "Text", "text": "list of" },
                { "id": "c3", "component": "Card", "child": "c3t" }, { "id": "c3t", "component": "Text", "text": "cards that" },
                { "id": "c4", "component": "Card", "child": "c4t" }, { "id": "c4t", "component": "Text", "text": "scrolls sideways" },
                { "id": "modal", "component": "Modal", "trigger": "modalButton", "content": "modalBody" },
                { "id": "modalButton", "component": "Button", "child": "modalButtonText" },
                { "id": "modalButtonText", "component": "Text", "text": "Open a modal" },
                { "id": "modalBody", "component": "Column", "children": ["modalTitle", "modalText"] },
                { "id": "modalTitle", "component": "Text", "variant": "h3", "text": "A modal" },
                { "id": "modalText", "component": "Text", "text": "The content is any component tree. Close with Escape or the backdrop." },

                { "id": "inputsTab", "component": "Column", "children": ["name", "email", "pin", "age", "bio", "agree", "size", "toppings", "volume", "date", "time", "both", "actions"] },
                { "id": "name", "component": "TextField", "label": "Name", "value": { "@path": "/user/name" },
                  "@checks": [ { "condition": { "@call": "length", "args": { "value": { "@path": "/user/name" }, "min": 2 } }, "message": "At least two characters" } ] },
                { "id": "email", "component": "TextField", "label": "Email", "placeholder": "you@example.com", "value": { "@path": "/user/email" },
                  "@checks": [ { "condition": { "@call": "email", "args": { "value": { "@path": "/user/email" } } }, "message": "Enter a valid email" } ] },
                { "id": "pin", "component": "TextField", "label": "PIN", "variant": "obscured", "value": { "@path": "/user/pin" } },
                { "id": "age", "component": "TextField", "label": "Age", "variant": "number", "value": { "@path": "/user/age" },
                  "@checks": [ { "condition": { "@call": "numeric", "args": { "value": { "@path": "/user/age" }, "min": 0, "max": 130 } }, "message": "0 to 130" } ] },
                { "id": "bio", "component": "TextField", "label": "Bio", "variant": "longText", "value": { "@path": "/user/bio" } },
                { "id": "agree", "component": "CheckBox", "label": "I agree to the terms", "value": { "@path": "/agree" } },
                { "id": "size", "component": "ChoicePicker", "label": "Size", "value": { "@path": "/size" },
                  "options": [ { "label": "Small", "value": "s" }, { "label": "Medium", "value": "m" }, { "label": "Large", "value": "l" } ] },
                { "id": "toppings", "component": "ChoicePicker", "label": "Toppings", "variant": "multipleSelection", "displayStyle": "chips", "filterable": true, "value": { "@path": "/toppings" },
                  "options": [ { "label": "Cheese", "value": "cheese" }, { "label": "Mushroom", "value": "mushroom" }, { "label": "Olives", "value": "olives" }, { "label": "Pepper", "value": "pepper" }, { "label": "Basil", "value": "basil" } ] },
                { "id": "volume", "component": "Slider", "label": "Volume", "min": 0, "max": 100, "steps": 20, "value": { "@path": "/volume" } },
                { "id": "date", "component": "DateTimeInput", "label": "Date", "enableDate": true, "value": { "@path": "/when/date" }, "min": "2026-01-01" },
                { "id": "time", "component": "DateTimeInput", "label": "Time", "enableTime": true, "value": { "@path": "/when/time" } },
                { "id": "both", "component": "DateTimeInput", "label": "Date and time", "enableDate": true, "enableTime": true, "value": { "@path": "/when/both" } },
                { "id": "actions", "component": "Row", "children": ["submit", "docs"] },
                { "id": "submit", "component": "Button", "variant": "primary", "child": "submitText",
                  "@checks": [ { "condition": { "@path": "/agree" }, "message": "Agree to the terms first" } ],
                  "action": { "event": { "name": "submitProfile", "context": { "name": { "@path": "/user/name" }, "email": { "@path": "/user/email" }, "size": { "@path": "/size" }, "toppings": { "@path": "/toppings" }, "volume": { "@path": "/volume" } } } } },
                { "id": "submitText", "component": "Text", "text": "Submit" },
                { "id": "docs", "component": "Button", "variant": "borderless", "child": "docsText",
                  "action": { "functionCall": { "@call": "openUrl", "args": { "url": "https://a2ui.org" } } } },
                { "id": "docsText", "component": "Text", "text": "Open a2ui.org" },

                { "id": "mediaTab", "component": "Column", "children": ["video", "audio"] },
                { "id": "video", "component": "Video", "url": "https://interactive-examples.mdn.mozilla.net/media/cc0-videos/flower.mp4" },
                { "id": "audio", "component": "AudioPlayer", "url": "https://interactive-examples.mdn.mozilla.net/media/cc0-audio/t-rex-roar.mp3", "description": "A T-rex roar" }
              ]
            }
          }
        ]
        """;
}
