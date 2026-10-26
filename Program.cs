using VrmGenerator;

switch (args)
{
    case []:
        GenerateOne();
        break;

    case ["--batch", var countText] when int.TryParse(countText, out var count) && count > 0:
        for (var i = 0; i < count; i++)
            Console.WriteLine(VrmCode.Generate());
        break;

    case ["--decode", var code]:
        Decode(code);
        break;

    case ["--help" or "-h" or "/?" or "help"]:
        PrintHelp();
        break;

    default:
        Console.WriteLine($"Unrecognised arguments: {string.Join(' ', args)}");
        Console.WriteLine();
        PrintHelp();
        return 1;
}

return 0;

static void GenerateOne()
{
    var generated = VrmCode.Generate();
    var decoded = VrmCode.Decode(generated);
    Console.WriteLine($"Machine:  {Environment.MachineName} (tag {VrmCode.MachineTag})");
    Console.WriteLine($"Code:     {generated}");
    Console.WriteLine($"Decoded:  {decoded.LocalTime:yyyy-MM-dd HH:mm:ss}  sequence {decoded.Sequence}");
}

static void Decode(string code)
{
    try
    {
        var (when, tag, seq) = VrmCode.Decode(code);
        var origin = tag == VrmCode.MachineTag
            ? $"generated on this machine ({Environment.MachineName})"
            : $"generated on a different machine (tag {tag}, this machine is {VrmCode.MachineTag})";
        Console.WriteLine(code.ToUpperInvariant());
        Console.WriteLine($"  when:     {when:dddd d MMMM yyyy HH:mm:ss} (local time)");
        Console.WriteLine($"  origin:   {origin}");
        Console.WriteLine($"  sequence: #{seq + 1} generated in that second");
        if (when > DateTime.Now.AddMinutes(5))
            Console.WriteLine("  warning:  that time is in the future, so this is probably not a generated code");
    }
    catch (FormatException ex)
    {
        Console.WriteLine($"Not a code from this generator: {ex.Message}");
    }
}

static void PrintHelp()
{
    Console.WriteLine($"""
        VrmGenerator - 10-character test VRM codes that encode when and where they were made.

        Usage:
          VrmGenerator                  Generate one code and show its decoded fields
          VrmGenerator --batch <n>      Generate n codes, one per line (nothing else printed)
          VrmGenerator --decode <code>  Show when a code was made (to the second), whether it
                                        came from this machine, and its sequence number
          VrmGenerator --help           Show this help

        Code layout (50 bits in Crockford Base32, no I/L/O/U):
          chars 1-6    seconds since 1 Jan 2020, local time (not readable by eye; use --decode)
          chars 7-8    machine tag, derived from the machine name (this machine: {VrmCode.MachineTag})
          chars 9-10   sequence within that second (00-ZZ, up to 1024 per second)
        """);
}
