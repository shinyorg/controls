// The analysis types are surfaced under their own xmlns and under both hosts' — so an analyzer or a CaptionView
// can be written as cam:BarcodeAnalyzer inside a CameraView or media:BarcodeAnalyzer inside a MediaElement without
// another namespace declaration.
[assembly: Microsoft.Maui.Controls.XmlnsDefinition("http://shiny.net/maui/analysis", "Shiny.Maui.Controls.Analysis")]
[assembly: Microsoft.Maui.Controls.XmlnsDefinition("http://shiny.net/maui/analysis", "Shiny.Controls.Analysis", AssemblyName = "Shiny.Controls.Analysis.Shared")]
[assembly: Microsoft.Maui.Controls.XmlnsDefinition("http://shiny.net/maui/camera", "Shiny.Maui.Controls.Analysis")]
[assembly: Microsoft.Maui.Controls.XmlnsDefinition("http://shiny.net/maui/camera", "Shiny.Controls.Analysis", AssemblyName = "Shiny.Controls.Analysis.Shared")]
[assembly: Microsoft.Maui.Controls.XmlnsDefinition("http://shiny.net/maui/media", "Shiny.Maui.Controls.Analysis")]
[assembly: Microsoft.Maui.Controls.XmlnsDefinition("http://shiny.net/maui/media", "Shiny.Controls.Analysis", AssemblyName = "Shiny.Controls.Analysis.Shared")]
[assembly: Microsoft.Maui.Controls.XmlnsPrefix("http://shiny.net/maui/analysis", "analysis")]
