namespace Gst.Api.Tests.Support;

// Program.cs static Serilog Log.Logger badalta hai — isliye host-based tests serial chalte hain
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class EndpointCollection
{
    public const string Name = "Endpoint";
}
