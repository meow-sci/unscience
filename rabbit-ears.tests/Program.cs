using System;
using RabbitEars.Tests;

EncoderChecks.Run();
SpectrumChecks.Run();
StreamingChecks.Run();
TunerChecks.Run();
NetChecks.Run();
SenderChecks.Run();
LiveChecks.Run();

Console.WriteLine($"\nrabbit-ears.tests: {Check.Passed} passed, {Check.Failures} failed");
return Check.Failures == 0 ? 0 : 1;
