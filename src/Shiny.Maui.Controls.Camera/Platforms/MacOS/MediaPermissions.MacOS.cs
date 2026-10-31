using AVFoundation;
using Shiny.Maui.Controls.Camera.Media;

namespace Shiny.Maui.Controls.Camera;

/// <summary>
/// The camera and microphone permissions on the AppKit head, asked of AVFoundation directly. That head's Essentials has
/// no implementation of <c>Permissions.Camera</c> or <c>Permissions.Microphone</c>: it answers
/// <c>PermissionStatus.Unknown</c> without prompting, so every capture and scan through <see cref="IMediaService"/> was
/// refused before the camera was ever asked. <see cref="AVCaptureDevice"/> is what the capture session consults anyway.
/// </summary>
static class MacMediaPermissions
{
    public static async Task<MediaPermissionStatus> RequestAsync(AVAuthorizationMediaType type)
    {
        switch (AVCaptureDevice.GetAuthorizationStatus(type))
        {
            case AVAuthorizationStatus.Authorized:
                return MediaPermissionStatus.Granted;

            case AVAuthorizationStatus.Restricted:
                return MediaPermissionStatus.Restricted;

            case AVAuthorizationStatus.Denied:
                return MediaPermissionStatus.Denied;

            default:
                // undecided: the only state macOS shows the prompt from
                return await AVCaptureDevice.RequestAccessForMediaTypeAsync(type).ConfigureAwait(false)
                    ? MediaPermissionStatus.Granted
                    : MediaPermissionStatus.Denied;
        }
    }
}
