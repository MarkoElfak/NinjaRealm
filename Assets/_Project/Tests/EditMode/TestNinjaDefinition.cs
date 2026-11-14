using NinjaRealm.Core.Content;

namespace NinjaRealm.Tests
{
    /// <summary>A definition type whose IDs must start with "ninja.", used only by tests.</summary>
    public sealed class TestNinjaDefinition : ContentDefinition
    {
        public override string IdCategory => "ninja";
    }
}
