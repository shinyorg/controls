using Shiny.Controls.Media;
using Shiny.Maui.Controls.Media;
using Shouldly;
using Xunit;

namespace Shiny.Maui.Controls.Media.Tests;

public class MediaCommandTests : MediaTestBase
{
    [Fact]
    public void Play_pause_stop_and_toggle_commands_drive_the_backend()
    {
        var element = new MediaElement().Connected();

        element.PlayCommand.Execute(null);
        element.PauseCommand.Execute(null);
        element.StopCommand.Execute(null);

        this.Backend.Calls.ShouldContain("Play");
        this.Backend.Calls.ShouldContain("Pause");
        this.Backend.Calls.ShouldContain("Stop");
    }

    [Fact]
    public void TogglePlayPause_follows_the_current_state()
    {
        var element = new MediaElement().Connected();

        element.TogglePlayPauseCommand.Execute(null);
        element.CurrentState.ShouldBe(MediaElementState.Playing);

        element.TogglePlayPauseCommand.Execute(null);
        element.CurrentState.ShouldBe(MediaElementState.Paused);
    }

    [Fact]
    public void A_bare_number_parameter_is_read_as_seconds()
    {
        // XAML can only hand a command a string. "30" has to mean thirty seconds — TimeSpan.Parse would
        // read it as thirty days.
        var element = new MediaElement().Connected();
        this.Backend.RaiseOpened(TimeSpan.FromHours(2));

        element.SeekCommand.Execute("30");

        this.Backend.Calls.ShouldContain($"Seek({TimeSpan.FromSeconds(30)})");
    }

    [Fact]
    public void A_colon_separated_parameter_is_read_as_a_timespan()
    {
        var element = new MediaElement().Connected();
        this.Backend.RaiseOpened(TimeSpan.FromHours(2));

        element.SeekCommand.Execute("00:01:30");

        this.Backend.Calls.ShouldContain($"Seek({TimeSpan.FromSeconds(90)})");
    }

    [Theory]
    [InlineData(45d)]
    [InlineData(45)]
    public void Numeric_parameters_are_read_as_seconds(object parameter)
    {
        var element = new MediaElement().Connected();
        this.Backend.RaiseOpened(TimeSpan.FromHours(2));

        element.SeekCommand.Execute(parameter);

        this.Backend.Calls.ShouldContain($"Seek({TimeSpan.FromSeconds(45)})");
    }

    [Fact]
    public void A_TimeSpan_parameter_is_used_directly()
    {
        var element = new MediaElement().Connected();
        this.Backend.RaiseOpened(TimeSpan.FromHours(2));

        element.SeekCommand.Execute(TimeSpan.FromMinutes(3));

        this.Backend.Calls.ShouldContain($"Seek({TimeSpan.FromMinutes(3)})");
    }

    [Fact]
    public void An_unparseable_seek_parameter_is_ignored_rather_than_seeking_to_zero()
    {
        var element = new MediaElement().Connected();
        this.Backend.RaiseOpened(TimeSpan.FromHours(2));

        element.SeekCommand.Execute("not-a-time");

        this.Backend.Calls.ShouldNotContain(c => c.StartsWith("Seek(", StringComparison.Ordinal));
    }

    [Fact]
    public void MuteCommand_toggles_without_a_parameter_and_sets_with_one()
    {
        var element = new MediaElement().Connected();

        element.MuteCommand.Execute(null);
        element.IsMuted.ShouldBeTrue();

        element.MuteCommand.Execute(false);
        element.IsMuted.ShouldBeFalse();

        element.MuteCommand.Execute("true");
        element.IsMuted.ShouldBeTrue();
    }

    [Fact]
    public void The_picture_in_picture_command_is_disabled_where_the_platform_cannot_do_it()
    {
        this.Backend.Capabilities = MediaPlaybackCapabilities.Volume;
        var element = new MediaElement().Connected();

        element.PictureInPictureCommand.CanExecute(null).ShouldBeFalse();
    }

    [Fact]
    public void The_picture_in_picture_command_is_enabled_where_the_platform_can()
    {
        this.Backend.Capabilities = MediaPlaybackCapabilities.PictureInPicture;
        var element = new MediaElement().Connected();

        element.PictureInPictureCommand.CanExecute(null).ShouldBeTrue();
    }
}
