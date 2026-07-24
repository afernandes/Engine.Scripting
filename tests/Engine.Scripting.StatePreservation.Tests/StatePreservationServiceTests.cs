using Engine.Scripting.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace Engine.Scripting.StatePreservation.Tests;

public class StatePreservationServiceTests
{
    private sealed class SampleScript : SampleScriptBase
    {
        [HotReloadState] private int _counter = 5;

        [HotReloadState("hp")]
        public float Health { get; set; } = 99.5f;

        [HotReloadState] public string? Name = "initial";

        public int NotPreserved = 1;

        public int GetCounter() => _counter;

        public void SetCounter(int value) => _counter = value;
    }

    private class SampleScriptBase
    {
        [HotReloadState] private int _baseValue = 3;

        public int GetBaseValue() => _baseValue;

        public void SetBaseValue(int value) => _baseValue = value;
    }

    private sealed class ReadOnlyPropertyScript
    {
        [HotReloadState] public int Broken { get; } = 9;
    }

    private sealed class IntCounterScript
    {
        // Assigned only through reflection by the service under test.
        [HotReloadState] private int _counter = 0;

        public int GetCounter() => _counter;
    }

    [Fact]
    public void Capture_MembrosMarcadosIncluindoHerdados_EntramNoSnapshotComSuasChaves()
    {
        var service = new StatePreservationService();
        var script = new SampleScript();
        script.SetCounter(42);
        script.SetBaseValue(7);
        script.Health = 12.25f;
        script.Name = "renamed";

        var snapshot = service.Capture(script);

        Assert.Equal(typeof(SampleScript).FullName, snapshot.TypeFullName);
        Assert.Equal(42, snapshot.Values["_counter"]);
        Assert.Equal(12.25f, snapshot.Values["hp"]);
        Assert.Equal("renamed", snapshot.Values["Name"]);
        Assert.Equal(7, snapshot.Values["_baseValue"]);
        Assert.DoesNotContain("NotPreserved", snapshot.Values.Keys);
        Assert.Empty(snapshot.DiscardedMembers);
        Assert.True(DateTimeOffset.UtcNow - snapshot.CapturedAtUtc < TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void Restore_SnapshotCompativel_AplicaValoresEmOutraInstancia()
    {
        var service = new StatePreservationService();
        var original = new SampleScript();
        original.SetCounter(100);
        original.SetBaseValue(200);
        original.Health = 55.5f;
        original.Name = "kept";

        var snapshot = service.Capture(original);
        var fresh = new SampleScript();
        service.Restore(fresh, snapshot);

        Assert.Equal(100, fresh.GetCounter());
        Assert.Equal(200, fresh.GetBaseValue());
        Assert.Equal(55.5f, fresh.Health);
        Assert.Equal("kept", fresh.Name);
        Assert.Equal(1, fresh.NotPreserved);
    }

    [Fact]
    public void Restore_ChaveDoSnapshotSemMembroNoTipoNovo_IgnoraELogaWarning()
    {
        var logger = new FakeLogger<StatePreservationService>();
        var service = new StatePreservationService(logger);
        var snapshot = new ScriptStateSnapshot(
            "Old.Type",
            new Dictionary<string, object?> { ["ghostField"] = 123 },
            [],
            DateTimeOffset.UtcNow);
        var fresh = new IntCounterScript();

        service.Restore(fresh, snapshot);

        Assert.Equal(0, fresh.GetCounter());
        Assert.Contains(logger.Collector.GetSnapshot(), r =>
            r.Level == LogLevel.Warning && r.Message.Contains("ghostField", StringComparison.Ordinal));
    }

    [Fact]
    public void Restore_TipoIncompativelEntreVersoes_IgnoraELogaWarning()
    {
        var logger = new FakeLogger<StatePreservationService>();
        var service = new StatePreservationService(logger);
        var snapshot = new ScriptStateSnapshot(
            "Old.Type",
            new Dictionary<string, object?> { ["_counter"] = "not an int anymore" },
            [],
            DateTimeOffset.UtcNow);
        var fresh = new IntCounterScript();

        service.Restore(fresh, snapshot);

        Assert.Equal(0, fresh.GetCounter());
        Assert.Contains(logger.Collector.GetSnapshot(), r =>
            r.Level == LogLevel.Warning && r.Message.Contains("_counter", StringComparison.Ordinal));
    }

    [Fact]
    public void Restore_NullEmValueTypeNaoNullable_IgnoraELogaWarning()
    {
        var logger = new FakeLogger<StatePreservationService>();
        var service = new StatePreservationService(logger);
        var snapshot = new ScriptStateSnapshot(
            "Old.Type",
            new Dictionary<string, object?> { ["_counter"] = null },
            [],
            DateTimeOffset.UtcNow);
        var fresh = new IntCounterScript();

        service.Restore(fresh, snapshot);

        Assert.Equal(0, fresh.GetCounter());
        Assert.Contains(logger.Collector.GetSnapshot(), r => r.Level == LogLevel.Warning);
    }

    [Fact]
    public void Capture_PropriedadeSomenteLeitura_NaoEntraNoSnapshotELogaWarning()
    {
        var logger = new FakeLogger<StatePreservationService>();
        var service = new StatePreservationService(logger);

        var snapshot = service.Capture(new ReadOnlyPropertyScript());

        Assert.Empty(snapshot.Values);
        Assert.Contains(logger.Collector.GetSnapshot(), r =>
            r.Level == LogLevel.Warning && r.Message.Contains("Broken", StringComparison.Ordinal));
    }

    [Fact]
    public void Capture_InstanciaForaDeAlcColetavel_MigraValoresDeTiposDoProprioAssembly()
    {
        // Host-side objects (and unit-test types) do not live in a collectible context, so there
        // is no generation to pin and even locally-declared types may migrate.
        var service = new StatePreservationService();
        var holder = new HostSideHolder { Payload = new HostPayload { Value = 9 } };

        var snapshot = service.Capture(holder);

        Assert.Empty(snapshot.DiscardedMembers);
        var payload = Assert.IsType<HostPayload>(snapshot.Values["Payload"]);
        Assert.Equal(9, payload.Value);
    }

    private sealed class HostSideHolder
    {
        [HotReloadState] public HostPayload? Payload { get; set; }
    }

    public sealed class HostPayload
    {
        public int Value { get; set; }
    }
}
