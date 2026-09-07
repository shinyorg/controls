using Shiny.Controls.FloorPlan;

namespace Sample.Features.FloorPlan;

/// <summary>
/// The office both floor plan demos draw: three rooms, a bank of twelve cubicles, a conference table
/// with chairs around it, doors, outlets and a partition wall.
/// </summary>
/// <remarks>
/// Deliberately not styled. Every element leaves its colours unset so the plan follows the app's
/// theme - which is the point worth demonstrating, and the thing a hard-coded palette would hide.
/// The conference room and the break room are the exception: an authored plan often does colour-code
/// its rooms, and those two show that an explicit colour is honoured in both schemes.
/// </remarks>
public static class SampleFloorPlan
{
    public static FloorPlanDocument CreateOffice()
    {
        var doc = new FloorPlanDocument
        {
            Width = 1200,
            Height = 900,
            GridSize = 20
        };

        doc.Elements.Add(new RoomElement
        {
            Name = "Main Office",
            Label = "Main Office",
            Transform = { X = 100, Y = 100 },
            Width = 500,
            Height = 400,
            Style = { StrokeWidth = 3 }
        });

        doc.Elements.Add(new RoomElement
        {
            Name = "Conference Room",
            Label = "Conference",
            Transform = { X = 650, Y = 100 },
            Width = 300,
            Height = 250,
            Style = { FillColor = "#E3F2FD", StrokeColor = "#1565C0", StrokeWidth = 3 }
        });

        doc.Elements.Add(new RoomElement
        {
            Name = "Break Room",
            Label = "Break Room",
            Transform = { X = 650, Y = 400 },
            Width = 300,
            Height = 200,
            Style = { FillColor = "#FFF3E0", StrokeColor = "#E65100", StrokeWidth = 3 }
        });

        var names = new[]
        {
            "Ada L.", "Grace H.", "Alan T.", "Edsger D.",
            "Barbara L.", "Donald K.", "Margaret H.", "Ken T.",
            "Linus T.", "Anders H.", "Rich H.", "Bjarne S."
        };

        for (var row = 0; row < 3; row++)
        {
            for (var col = 0; col < 4; col++)
            {
                var index = row * 4 + col;
                doc.Elements.Add(new CubicleElement
                {
                    Name = $"Desk {index + 1}",
                    Occupant = names[index],
                    Transform = { X = 120 + col * 100, Y = 150 + row * 100 },
                    Width = 80,
                    Height = 80
                });
            }
        }

        doc.Elements.Add(new FurnitureElement
        {
            Kind = FurnitureKind.Table,
            Name = "Conference Table",
            Transform = { X = 720, Y = 180 },
            Width = 160,
            Height = 80
        });

        for (var i = 0; i < 6; i++)
        {
            doc.Elements.Add(new FurnitureElement
            {
                Kind = FurnitureKind.Chair,
                Name = $"Conference Chair {i + 1}",
                Transform = { X = 730 + i % 3 * 50, Y = i < 3 ? 145 : 270 },
                Width = 30,
                Height = 30
            });
        }

        doc.Elements.Add(new FurnitureElement
        {
            Kind = FurnitureKind.Sofa,
            Name = "Break Room Sofa",
            Transform = { X = 690, Y = 470 },
            Width = 120,
            Height = 50
        });

        doc.Elements.Add(new DoorElement
        {
            Name = "Main Door",
            Transform = { X = 300, Y = 100 },
            Width = 40,
            DoorType = DoorType.Double
        });

        doc.Elements.Add(new DoorElement
        {
            Name = "Conference Door",
            Transform = { X = 650, Y = 200, Rotation = 90 },
            Width = 35
        });

        doc.Elements.Add(new WallElement
        {
            Name = "Partition",
            Start = new PlanPoint(100, 550),
            End = new PlanPoint(600, 550),
            Thickness = 8
        });

        doc.Elements.Add(new OutletElement { Name = "Outlet A", Transform = { X = 140, Y = 110 } });
        doc.Elements.Add(new OutletElement { Name = "Outlet B", Transform = { X = 340, Y = 110 } });
        doc.Elements.Add(new OutletElement
        {
            Name = "Conference Data Port",
            OutletType = OutletType.Data,
            Transform = { X = 800, Y = 120 }
        });
        doc.Elements.Add(new OutletElement
        {
            Name = "Floor Box",
            OutletType = OutletType.Floor,
            Transform = { X = 800, Y = 260 }
        });

        return doc;
    }
}
