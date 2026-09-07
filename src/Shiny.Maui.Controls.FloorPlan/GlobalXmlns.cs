// Its own XAML namespace, the way every other add-on package has one. The shared engine's types are
// mapped into it too, so a tool declared in XAML - <fp:DrawRoomTool /> - needs no second prefix even
// though it lives in Shiny.Controls.FloorPlan rather than here.
[assembly: Microsoft.Maui.Controls.XmlnsDefinition("http://shiny.net/maui/floorplan", "Shiny.Maui.Controls.FloorPlan")]
[assembly: Microsoft.Maui.Controls.XmlnsDefinition("http://shiny.net/maui/floorplan", "Shiny.Controls.FloorPlan")]
