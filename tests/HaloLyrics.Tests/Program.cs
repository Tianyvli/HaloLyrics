using HaloLyrics;
using System.Text;
using System.Text.Json;

var count = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAIL: " + name);
    Console.WriteLine("PASS: " + name); count++;
}
var packet = PixelBar.Packet(0xEC, 0xEE, []);
Check(Convert.ToHexString(packet.AsSpan(0, 7)) == "2EAAECEE000084", "A160 state query wire frame");
var text = "原詞：你好 🌸";
var bytes = Encoding.UTF8.GetBytes(text);
packet = PixelBar.Packet(0xEC, 0xE8, [0, (byte)bytes.Length, .. bytes]);
Check(packet.Length == 64 && packet[5] == bytes.Length + 2, "UTF-8 payload byte length");
Check(Encoding.UTF8.GetString(packet.AsSpan(8, bytes.Length)) == text, "Original Unicode preserved");
Check(packet[8+bytes.Length] == (byte)packet.Skip(1).Take(7+bytes.Length).Sum(x => x), "Text packet checksum");
try { PixelBar.Packet(0xEC, 0xE8, new byte[58]); throw new Exception("oversized packet accepted"); }
catch (ArgumentException) { Check(true, "Oversized packet rejected"); }
var ttml = """
<tt xmlns="http://www.w3.org/ns/ttml"><body><div>
<p begin="00:01.250" end="00:03.500"><span begin="1.25s" end="2s">原詞</span> <span begin="2s" end="3.5s">不轉簡體🌸</span></p>
<p begin="4s" end="5500ms">second line</p>
</div></body></tt>
""";
var lines = Lyrics.Parse(ttml);
Check(lines.Count == 2 && lines[0].Begin == 1.25 && lines[1].End == 5.5, "TTML clock/seconds/milliseconds timing");
Check(lines[0].Text == "原詞 不轉簡體🌸", "TTML nested span whitespace and original script");
Check(Lyrics.Parse("<tt><body><p>No timing</p></body></tt>").Count == 0, "Untimed lyrics do not pretend to synchronize");
using var json = JsonDocument.Parse(JsonSerializer.Serialize(new { data = new[] { new { attributes = new { ttml } } } }));
Check(Lyrics.ExtractTtml(json.RootElement) == ttml, "Apple response TTML extraction");
try { Lyrics.Parse("<!DOCTYPE tt [<!ENTITY x SYSTEM 'file:///C:/secret'>]><tt><body><p begin='0' end='1'>&x;</p></body></tt>"); throw new Exception("DTD accepted"); }
catch (System.Xml.XmlException) { Check(true, "TTML external entities rejected"); }
var original = "這是一段超過十六字的原始歌詞，需要完整顯示。 An English line with e\u0301 and 🌸.";
var segments = TextSegments.Split(original);
Check(segments.Count > 1 && string.Concat(segments) == original, "Long lines preserve every original character");
Check(segments.All(x => Encoding.UTF8.GetByteCount(x) <= 48 && x.EnumerateRunes().Count() <= 16), "Every segment fits A160 UTF-8 and character limits");
Check(segments.All(x => !x.StartsWith('\u0301') && !char.IsLowSurrogate(x[0]) && !char.IsHighSurrogate(x[^1])), "Segmentation preserves graphemes and surrogate pairs");
var state = new byte[] {0,16,50,6,0xE5,0x8E,0x9F,0xE8,0xA9,0x9E};
Check(Encoding.UTF8.GetString(PixelBar.ParseTextState(state).AsSpan(2)) == "原詞", "A160 capacity fields are not mistaken for display text");
try { PixelBar.ParseTextState([0,16,50,10,65]); throw new Exception("Invalid text state accepted"); }
catch(IOException) { Check(true, "Malformed text readback rejected"); }
var fixture = new List<LyricLine> {new(1,3,"first"),new(5,7,"second")};
Check(TextSegments.Current(fixture,0) is null && TextSegments.Current(fixture,3) is null, "Prelude and gaps clear old lyric");
Check(TextSegments.Current(fixture,6)?.Text == "second" && TextSegments.Current(fixture,2)?.Text == "first", "Seeking backward does not retain a stale lyric");
var adjacent = new List<LyricLine>{new(1,3,"first"),new(3.2,5,"second")};
Check(TextSegments.Current(adjacent,3.1)?.Text == "first" && TextSegments.Current(adjacent,3.2)?.Text == "second", "Short vocal gaps do not flash a blank frame");
var cachedLine = new LyricLine(1, 5, "原詞");
var originalHash = cachedLine.GetHashCode();
Check(TextSegments.At(cachedLine, 2) == "原詞" && TextSegments.At(cachedLine with { Text = "新詞" }, 2) == "新詞", "Cached segmentation cannot retain replaced lyric text");
Check(cachedLine == new LyricLine(1, 5, "原詞") && cachedLine.GetHashCode() == originalHash, "Rendering caches preserve lyric equality and hash codes");
Check(Interlude.ShouldAnimate(fixture,4,10,true,3) == false, "Short gaps do not activate the rhythm scene");
var longGap=new List<LyricLine>{new(8,12,"first"),new(22,25,"second")};
Check(Interlude.ShouldAnimate(longGap,2,30,true,5), "Long prelude activates rhythm scene");
Check(Interlude.ShouldAnimate(longGap,15,30,true,5), "Long interlude activates rhythm scene");
Check(!Interlude.ShouldAnimate(longGap,22,30,true,5), "Next vocal line immediately restores lyrics");
Check(!Interlude.ShouldAnimate(longGap,15,30,false,5), "Paused playback stops interlude animation");
Check(!Interlude.ShouldAnimate([],15,30,true,5), "Missing lyrics do not masquerade as a known interlude");
Check(PixelBar.ParseTextState([14,80,50,0],PixelBar.LyricSource).SequenceEqual(new byte[]{14,0}), "Native lyric buffer can be restored independently of custom text");
var track = new Playback("Song", "Artist", "Album", 200, 0, true, "AppleMusic", DateTimeOffset.UtcNow);
Check(track.SameTrack(track with { Position = 50, Playing = false }), "Position and pause changes preserve song identity");
Check(!track.SameTrack(track with { Album = "Other" }) && !track.SameTrack(track with { Duration = 210 }), "Album and duration changes invalidate song identity");
Check(!Interlude.ShouldAnimate(longGap, 27, 30, true, 8) && Interlude.ShouldAnimate(longGap, 27, 30, true, 5), "Outro respects the configurable interlude threshold");
Check(AppleLyricsClient.ExactMatch(track,new("1","Song","Artist","Album",200.2,true)), "Exact recording metadata matches");
Check(!AppleLyricsClient.ExactMatch(track,new("1","Song","Artist","Live Album",200.2,true)), "Different album requires explicit confirmation");
Check(!AppleLyricsClient.ExactMatch(track,new("1","Song","Artist","Album",210,true)), "Different duration requires explicit confirmation");
var clock = new PlaybackClock();
var now = DateTimeOffset.UtcNow;
clock.Apply(track with {Position=10,ObservedAt=now});
var smoothed=clock.Apply(track with {Position=9.7,ObservedAt=now.AddSeconds(0.1)});
Check(smoothed.Position >= 10 && smoothed.Position <= 10.2, "SMTC timestamp jitter cannot revisit an earlier line");
Check(clock.Apply(track with {Position=3,ObservedAt=now.AddSeconds(0.2)}).Position == 3, "Real backward seek resets immediately");
Check(clock.Apply(track with {Position=30,ObservedAt=now.AddSeconds(0.3)}).Position == 30, "Real forward seek resets immediately");
Check(clock.Apply(track with {Position=30,Playing=false,ObservedAt=now.AddSeconds(0.4)}).Position == 30
    && clock.Apply(track with {Position=30,Playing=false,ObservedAt=now.AddSeconds(2)}).Position == 30, "Paused playback clock does not advance");
foreach(var code in new[]{System.Net.HttpStatusCode.Unauthorized,System.Net.HttpStatusCode.Forbidden})
{
    using var api=new AppleLyricsClient(new("Bearer test-only","test-only"),new FakeHandler(_=>new(code)));
    try { await api.InitializeAsync(CancellationToken.None); throw new Exception("Expired credentials accepted"); }
    catch(AppleLoginException e){Check(!e.Message.Contains("test-only"),$"HTTP {(int)code} requests reauthentication without leaking credentials");}
}
using(var api=new AppleLyricsClient(new("Bearer test-only","test-only"),new FakeHandler(_=>new(System.Net.HttpStatusCode.OK){Content=new StringContent("{\"data\":[{\"id\":\"cn\"}]}")})))
{
    using var cancellation=new CancellationTokenSource(); cancellation.Cancel();
    try { await api.InitializeAsync(cancellation.Token); throw new Exception("Cancellation ignored"); }
    catch(OperationCanceledException){Check(true,"Cancelled lyric requests do not complete");}
}
if (args.Contains("--benchmark"))
{
    var sample = new LyricLine(1, 10, "這是一段需要完整顯示的原始歌詞 with an English phrase and e\u0301 🌸");
    var sampleLines = new List<LyricLine> { sample, new(20, 25, "next") };
    for (var i = 0; i < 1000; i++) { _ = TextSegments.At(sample, 5); _ = Interlude.ShouldAnimate(sampleLines, 15, 30, true, 5); }
    var allocated = GC.GetAllocatedBytesForCurrentThread();
    var watch = System.Diagnostics.Stopwatch.StartNew();
    for (var i = 0; i < 10000; i++) { _ = TextSegments.At(sample, 5); _ = Interlude.ShouldAnimate(sampleLines, 15, 30, true, 5); }
    var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
    Console.WriteLine(JsonSerializer.Serialize(new { benchmark = "render", iterations = 10000,
        allocatedBytes, milliseconds = watch.Elapsed.TotalMilliseconds }));
    Check(allocatedBytes < 1024, "Repeated lyric rendering and interlude checks stay below 1 KB allocation per 10000 frames");
}
Console.WriteLine($"{count} checks passed.");

sealed class FakeHandler(Func<HttpRequestMessage,HttpResponseMessage> reply) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(reply(request));
    }
}
