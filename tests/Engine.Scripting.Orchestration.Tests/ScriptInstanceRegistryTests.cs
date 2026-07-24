using Engine.Scripting.Instances;

namespace Engine.Scripting.Orchestration.Tests;

public class ScriptInstanceRegistryTests
{
    [Fact]
    public void Register_Instancia_GeraHandleComTipoEResolveInstancia()
    {
        var registry = new ScriptInstanceRegistry();
        var instance = new object();

        var handle = registry.Register(instance);

        Assert.NotEqual(Guid.Empty, handle.Id);
        Assert.Equal("System.Object", handle.TypeFullName);
        Assert.Same(instance, registry.GetAs<object>(handle));
        Assert.True(registry.TryGetInstance(handle, out var resolved));
        Assert.Same(instance, resolved);
    }

    [Fact]
    public void GetAs_TipoIncompativel_RetornaNull()
    {
        var registry = new ScriptInstanceRegistry();
        var handle = registry.Register("a string instance");

        Assert.Null(registry.GetAs<ICounterScript>(handle));
        Assert.NotNull(registry.GetAs<string>(handle));
    }

    [Fact]
    public void DetachAll_InstanciasVivas_HandlesPermanecemSemInstancia()
    {
        var registry = new ScriptInstanceRegistry();
        var handle = registry.Register(new object());

        registry.DetachAll();

        Assert.Single(registry.Handles);
        Assert.Null(registry.GetAs<object>(handle));
        Assert.False(registry.TryGetInstance(handle, out _));
        Assert.Empty(registry.GetLiveEntries());
    }

    [Fact]
    public void Attach_AposDetach_TrocaInstanciaPorTrasDoMesmoHandle()
    {
        var registry = new ScriptInstanceRegistry();
        var first = new List<int>();
        var handle = registry.Register(first);

        registry.DetachAll();
        var second = new List<int>();
        var attached = registry.Attach(handle, second);

        Assert.True(attached);
        Assert.Same(second, registry.GetAs<List<int>>(handle));
    }

    [Fact]
    public void Attach_HandleDesconhecido_RetornaFalse()
    {
        var registry = new ScriptInstanceRegistry();
        var unknown = new ScriptHandle(Guid.NewGuid(), "Ghost");

        Assert.False(registry.Attach(unknown, new object()));
    }

    [Fact]
    public void Unregister_HandleRegistrado_RemoveIdentidade()
    {
        var registry = new ScriptInstanceRegistry();
        var handle = registry.Register(new object());

        Assert.True(registry.Unregister(handle));
        Assert.False(registry.Unregister(handle));
        Assert.Empty(registry.Handles);
        Assert.Null(registry.GetAs<object>(handle));
    }

    [Fact]
    public void GetLiveEntries_MisturaDeVivosEDetachados_RetornaApenasVivos()
    {
        var registry = new ScriptInstanceRegistry();
        var live = registry.Register(new object());
        var detached = registry.Register(new object());

        registry.DetachAll();
        registry.Attach(live, new object());

        var entries = registry.GetLiveEntries();
        var entry = Assert.Single(entries);
        Assert.Equal(live.Id, entry.Key.Id);
        Assert.NotEqual(detached.Id, entry.Key.Id);
    }
}
