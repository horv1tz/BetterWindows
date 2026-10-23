using System.Runtime.CompilerServices;

// The pure logic of the surfaces stream (geometry, planning, auto-hide state machine, visibility reasons ...) is internal and is unit-tested from
// tests/BetterWindows.Windows.Tests/HostSurfaces without instantiating a single WPF object.
[assembly: InternalsVisibleTo("BetterWindows.Windows.Tests")]
