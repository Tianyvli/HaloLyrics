#nullable enable
using Windows.Media.Control;
using Windows.Foundation;
using WinRT;
using WinRT.Interop;
using PlaybackMethods = ABI.Windows.Media.Control.IGlobalSystemMediaTransportControlsSessionPlaybackInfoMethods;
using TimelineMethods = ABI.Windows.Media.Control.IGlobalSystemMediaTransportControlsSessionTimelinePropertiesMethods;
using MetadataMethods = ABI.Windows.Media.Control.IGlobalSystemMediaTransportControlsSessionMediaPropertiesMethods;

namespace HaloLyrics;

// Read primitive values and release owned snapshots immediately instead of waiting for GC.
[System.Runtime.Versioning.SupportedOSPlatform("windows10.0.19041.0")]
internal static class MediaSnapshot
{
    private static readonly Guid MetadataOperationId = GuidGenerator.CreateIID(typeof(IAsyncOperation<GlobalSystemMediaTransportControlsSessionMediaProperties>));

    public static Task<(string Title, string Artist, string Album)> ReadMetadataAsync(IAsyncOperation<GlobalSystemMediaTransportControlsSessionMediaProperties> operation)
    {
        var completion = new TaskCompletionSource<(string, string, string)>(TaskCreationOptions.RunContinuationsAsynchronously);
        operation.Completed = (op, _) =>
        {
            try
            {
                using var asyncReference = ((IWinRTObject)op).NativeObject.As<IUnknownVftbl>(MetadataOperationId);
                // IAsyncOperation<T>.GetResults is slot 8; the result is an owned media snapshot.
                using var media = Get(asyncReference, 8, MetadataMethods.IID);
                completion.TrySetResult((MetadataMethods.get_Title(media), MetadataMethods.get_Artist(media), MetadataMethods.get_AlbumTitle(media)));
            }
            catch (Exception e) { completion.TrySetException(e); }
            finally { op.Close(); }
        };
        return completion.Task;
    }

    public static (bool Playing, double Rate, double Position, double Duration, DateTimeOffset Updated) Read(GlobalSystemMediaTransportControlsSession session)
    {
        var reference = ((IWinRTObject)session).NativeObject;
        using var state = Get(reference, 9, PlaybackMethods.IID);
        using var timeline = Get(reference, 8, TimelineMethods.IID);
        return (PlaybackMethods.get_PlaybackStatus(state) == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
            PlaybackMethods.get_PlaybackRate(state) ?? 1,
            TimelineMethods.get_Position(timeline).TotalSeconds,
            TimelineMethods.get_EndTime(timeline).TotalSeconds,
            TimelineMethods.get_LastUpdatedTime(timeline));
    }

    private static unsafe IObjectReference Get(IObjectReference reference, int slot, Guid iid)
    {
        var pointer = reference.ThisPtr;
        nint result = 0;
        try
        {
            // IGlobalSystemMediaTransportControlsSession's SDK-defined slots:
            // 8 = GetTimelineProperties, 9 = GetPlaybackInfo.
            ExceptionHelpers.ThrowExceptionForHR((*(delegate* unmanaged[Stdcall]<nint, nint*, int>**)pointer)[slot](pointer, &result));
            GC.KeepAlive(reference);
            if (result == 0) throw new InvalidOperationException("Apple Music 未返回播放状态。");
            using var owned = ObjectReference<IUnknownVftbl>.Attach(ref result);
            return owned.As<IUnknownVftbl>(iid);
        }
        finally { if (result != 0) System.Runtime.InteropServices.Marshal.Release(result); }
    }
}
