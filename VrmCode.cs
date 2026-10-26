using System.Text;

namespace VrmGenerator;

/// <summary>
/// 10-character test VRM code: 50 bits packed into Crockford Base32 (no I/L/O/U).
///
///   bits 49-20 (chars 1-6)   seconds since 2020-01-01 00:00:00 UTC (30 bits, to 2054)
///   bits 19-10 (chars 7-8)   machine tag, stable hash of the machine name (10 bits)
///   bits  9-0  (chars 9-10)  sequence within that second, persisted on disk (10 bits)
///
/// Time is stored as UTC so clock changes (daylight saving) can never repeat a second;
/// Decode converts it back to local time. The date is not readable by eye; use Decode.
/// The machine tag and sequence are the last four characters as-is.
/// </summary>
public static class VrmCode
{
    public const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    public static readonly DateTime Epoch = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private const long MaxTimestamp = (1L << 30) - 1;
    private const int MaxSequence = 1023;

    private static readonly Lock SequenceLock = new();
    private static long _memTimestamp = -1;
    private static int _memSequence = -1;
    private static readonly string SequenceFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VrmGenerator", "sequence.txt");

    /// <summary>Two-character tag derived from this machine's name.</summary>
    public static string MachineTag { get; } = TagFromMachineName(Environment.MachineName);

    /// <summary>Generates a code that is unique across every process on this machine.</summary>
    public static string Generate()
    {
        var (timestamp, sequence) = Reserve();
        return ToBase32(timestamp, 6) + MachineTag + ToBase32(sequence, 2);
    }

    public static (DateTime LocalTime, string MachineTag, int Sequence) Decode(string code)
    {
        code = code.ToUpperInvariant();
        if (code.Length != 10 || code.Any(c => !Alphabet.Contains(c)))
            throw new FormatException($"'{code}' is not a 10-character code in the generator's alphabet.");

        var localTime = Epoch.AddSeconds(FromBase32(code[..6])).ToLocalTime();
        return (localTime, code[6..8], (int)FromBase32(code[8..10]));
    }

    /// <summary>True when the code decodes, its machine tag matches this machine, and its time is not in the future.</summary>
    public static bool IsFromThisMachine(string code)
    {
        try
        {
            var (when, tag, _) = Decode(code);
            return tag == MachineTag && when <= DateTime.Now.AddMinutes(5);
        }
        catch (FormatException) { return false; }
    }

    /// <summary>Stable 2-character tag for a machine name (FNV-1a hash, 10 bits).</summary>
    public static string TagFromMachineName(string machineName)
    {
        uint hash = 2166136261;
        foreach (var b in Encoding.UTF8.GetBytes(machineName.ToUpperInvariant()))
        {
            hash ^= b;
            hash *= 16777619;
        }
        return ToBase32(hash % 1024, 2);
    }

    /// <summary>
    /// Reserves a (timestamp, sequence) pair that no other call on this machine can get.
    /// The clock is read while the shared file is exclusively locked, and the stored timestamp
    /// never moves backwards, so two processes straddling a second boundary cannot both reset
    /// the counter. If a second fills up (1024 codes) the next second is borrowed instead.
    /// </summary>
    private static (long Timestamp, int Sequence) Reserve()
    {
        lock (SequenceLock)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SequenceFile)!);

            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    using var file = new FileStream(SequenceFile, FileMode.OpenOrCreate,
                        FileAccess.ReadWrite, FileShare.None);

                    var raw = new StreamReader(file, leaveOpen: true).ReadToEnd();
                    var parts = raw.Split(' ');
                    long lastTimestamp = -1;
                    var lastSequence = -1;
                    if (parts.Length != 2
                        || !long.TryParse(parts[0], out lastTimestamp)
                        || !int.TryParse(parts[1], out lastSequence))
                    {
                        if (raw.Length > 0)
                            Console.Error.WriteLine($"warning: sequence file was unreadable ('{raw}'); continuing from in-memory state");
                        lastTimestamp = -1;
                        lastSequence = -1;
                    }

                    // Never go below what this process has already handed out, even if the file misreads.
                    if (_memTimestamp > lastTimestamp || (_memTimestamp == lastTimestamp && _memSequence > lastSequence))
                    {
                        lastTimestamp = _memTimestamp;
                        lastSequence = _memSequence;
                    }

                    var timestamp = Math.Max(ToTimestamp(DateTime.UtcNow), lastTimestamp);
                    var sequence = timestamp == lastTimestamp ? lastSequence + 1 : 0;
                    if (sequence > MaxSequence)
                    {
                        timestamp++;
                        sequence = 0;
                    }
                    if (timestamp > MaxTimestamp)
                        throw new InvalidOperationException("Time is outside the encodable range (2020 to 2054).");

                    file.SetLength(0);
                    using var writer = new StreamWriter(file);
                    writer.Write($"{timestamp} {sequence}");
                    writer.Flush();
                    _memTimestamp = timestamp;
                    _memSequence = sequence;
                    return (timestamp, sequence);
                }
                catch (IOException) when (attempt < 400)
                {
                    // Another process holds the file; wait briefly and retry.
                    Thread.Sleep(5);
                }
            }
        }
    }

    private static long ToTimestamp(DateTime utcTime)
    {
        var seconds = (long)(utcTime - Epoch).TotalSeconds;
        if (seconds < 0)
            throw new InvalidOperationException($"Time {utcTime:u} is before the 2020 epoch.");
        return seconds;
    }

    private static string ToBase32(long value, int width)
    {
        Span<char> buffer = stackalloc char[width];
        for (var i = width - 1; i >= 0; i--)
        {
            buffer[i] = Alphabet[(int)(value & 31)];
            value >>= 5;
        }
        if (value != 0)
            throw new ArgumentOutOfRangeException(nameof(value), "Value does not fit in width.");
        return new string(buffer);
    }

    private static long FromBase32(string text)
    {
        long value = 0;
        foreach (var c in text)
            value = (value << 5) | (uint)Alphabet.IndexOf(c);
        return value;
    }
}
