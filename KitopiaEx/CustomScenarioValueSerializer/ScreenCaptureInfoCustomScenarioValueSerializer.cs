using System;
using Core.SDKs.CustomScenario;

namespace KitopiaEx.CustomScenarioValueSerializer;

public class ScreenCaptureInfoCustomScenarioValueSerializer : ICustomScenarioValueSerializer
{
    public string Serialize<T>(T value)
    {
        return null;
    }

    public object Deserialize(ReadOnlySpan<byte> value)
    {
        return null;
    }
}