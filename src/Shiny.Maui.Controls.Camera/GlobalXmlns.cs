[assembly: Microsoft.Maui.Controls.XmlnsDefinition("http://shiny.net/maui/camera", "Shiny.Maui.Controls.Camera")]
// Surface the shared types (BarcodeFormat, OverlayBox, VideoFrameFormat, …) under the same xmlns so they're
// usable in XAML as cam:* without a separate clr-namespace declaration.
[assembly: Microsoft.Maui.Controls.XmlnsDefinition("http://shiny.net/maui/camera", "Shiny.Controls.Camera", AssemblyName = "Shiny.Controls.Camera.Shared")]
[assembly: Microsoft.Maui.Controls.XmlnsPrefix("http://shiny.net/maui/camera", "cam")]
