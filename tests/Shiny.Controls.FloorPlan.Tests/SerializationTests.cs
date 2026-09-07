namespace Shiny.Controls.FloorPlan.Tests;

public class SerializationTests
{
    [Fact]
    public void EveryElementTypeRoundTrips()
    {
        var doc = Plan.Empty();
        doc.Elements.Add(Plan.Room(10, 20, 100, 80, "Boardroom"));
        doc.Elements.Add(new WallElement { Start = new PlanPoint(0, 0), End = new PlanPoint(100, 0), Thickness = 8 });
        doc.Elements.Add(new DoorElement { DoorType = DoorType.Sliding, Width = 44 });
        doc.Elements.Add(Plan.Cubicle(50, 50, "Ada"));
        doc.Elements.Add(new OutletElement { OutletType = OutletType.Data });
        doc.Elements.Add(new FurnitureElement { Kind = FurnitureKind.Sofa });
        doc.Elements.Add(new CustomElement { ShapeDefinitionId = "plant" });

        var back = FloorPlanSerializer.DeserializeDocument(FloorPlanSerializer.SerializeDocument(doc));

        back.ShouldNotBeNull();
        back!.Elements.Select(x => x.GetType()).ShouldBe(doc.Elements.Select(x => x.GetType()));
        back.Elements.OfType<RoomElement>().Single().Label.ShouldBe("Boardroom");
        back.Elements.OfType<DoorElement>().Single().DoorType.ShouldBe(DoorType.Sliding);
        back.Elements.OfType<CubicleElement>().Single().Occupant.ShouldBe("Ada");
    }


    /// <summary>
    /// The obvious economy - dropping every default as well as every null - is a trap: IsVisible and
    /// Opacity both default to the *non*-default CLR value, so a hidden element would serialize to
    /// nothing and come back visible.
    /// </summary>
    [Fact]
    public void AHiddenElementComesBackHidden()
    {
        var doc = Plan.Empty();
        var room = Plan.Room(0, 0);
        room.IsVisible = false;
        room.Style.Opacity = 0;
        doc.Elements.Add(room);

        var back = FloorPlanSerializer.DeserializeDocument(FloorPlanSerializer.SerializeDocument(doc))!;

        var restored = back.Elements.OfType<RoomElement>().Single();
        restored.IsVisible.ShouldBeFalse();
        restored.Style.Opacity.ShouldBe(0);
    }


    [Fact]
    public void MetadataAndStencilsSurvive()
    {
        var doc = Plan.Empty();
        doc.CustomShapes.Add(new ShapeDefinition { Id = "plant", Name = "Plant", SvgPath = "M0 0 L10 0 L10 10 Z" });

        var room = Plan.Room(0, 0);
        room.Metadata["capacity"] = "12";
        doc.Elements.Add(room);

        var back = FloorPlanSerializer.DeserializeDocument(FloorPlanSerializer.SerializeDocument(doc))!;

        back.CustomShapes.Single().SvgPath.ShouldBe("M0 0 L10 0 L10 10 Z");
        back.Elements.Single().Metadata["capacity"].ShouldBe("12");
    }


    [Fact]
    public void ABuildingRoundTripsItsFloors()
    {
        var building = new Building { Name = "HQ" };
        building.Floors.Add(new Floor { Name = "Ground", Level = 0, Plan = Plan.Empty() });
        building.Floors.Add(new Floor { Name = "Basement", Level = -1, Plan = Plan.Empty() });

        var back = FloorPlanSerializer.DeserializeBuilding(FloorPlanSerializer.SerializeBuilding(building))!;

        back.Name.ShouldBe("HQ");
        back.Floors.Select(x => x.Level).ShouldBe([0, -1]);
    }
}
