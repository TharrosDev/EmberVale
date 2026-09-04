using System;
using Embervale.Core.Services;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// The ownership rules the whole lifetime model rests on. These are deliberately about the
/// registry's <em>shape</em>, not about any particular service: what a scope does when it is
/// disposed, and which scope wins when two hold the same type.
/// </summary>
public class ServiceScopeTests
{
    private sealed class Clock
    {
        public string Name { get; init; } = "";
    }

    private sealed class Ledger;

    private static ServiceScope?[] Chain(ServiceScope? app, ServiceScope? session, ServiceScope? world)
        => new[] { app, session, world };

    [Fact]
    public void ResolvesFromTheScopeThatHoldsIt()
    {
        var app = new ServiceScope(ServiceLifetime.Application);
        var clock = new Clock { Name = "app" };
        app.Register(clock);

        Assert.Same(clock, ServiceLocator.ResolveFrom(Chain(app, null, null), typeof(Clock)));
    }

    [Fact]
    public void InnermostScopeShadowsOuterOnes()
    {
        var app = new ServiceScope(ServiceLifetime.Application);
        var session = new ServiceScope(ServiceLifetime.Session);
        var world = new ServiceScope(ServiceLifetime.World);

        app.Register(new Clock { Name = "app" });
        session.Register(new Clock { Name = "session" });
        var worldClock = new Clock { Name = "world" };
        world.Register(worldClock);

        Assert.Same(worldClock, ServiceLocator.ResolveFrom(Chain(app, session, world), typeof(Clock)));
    }

    [Fact]
    public void OuterScopeStillAnswersWhenTheInnerOneDoesNotHoldTheType()
    {
        var app = new ServiceScope(ServiceLifetime.Application);
        var world = new ServiceScope(ServiceLifetime.World);
        var clock = new Clock();
        app.Register(clock);
        world.Register(new Ledger());

        Assert.Same(clock, ServiceLocator.ResolveFrom(Chain(app, null, world), typeof(Clock)));
    }

    [Fact]
    public void DisposingAScopeRemovesExactlyItsOwnRegistrations()
    {
        var app = new ServiceScope(ServiceLifetime.Application);
        var session = new ServiceScope(ServiceLifetime.Session);
        var appClock = new Clock { Name = "app" };
        app.Register(appClock);
        session.Register(new Ledger());
        session.Register(new Clock { Name = "session" });

        Assert.Equal(2, session.Count);
        session.Dispose();

        Assert.Equal(0, session.Count);
        Assert.Equal(1, app.Count);
        // The session's Clock is gone; the application's is untouched and answers again.
        Assert.Same(appClock, ServiceLocator.ResolveFrom(Chain(app, session, null), typeof(Clock)));
        Assert.Null(ServiceLocator.ResolveFrom(Chain(app, session, null), typeof(Ledger)));
    }

    [Fact]
    public void DisposeIsIdempotent()
    {
        var scope = new ServiceScope(ServiceLifetime.Session);
        scope.Register(new Ledger());
        scope.Dispose();
        scope.Dispose();

        Assert.Equal(0, scope.Count);
    }

    [Fact]
    public void UnregisterOnlyRemovesTheInstanceItWasGiven()
    {
        // A replaced actor tearing down must not evict the successor that already took its slot,
        // which is the ordering a respawn produces: the new body registers, then the old body's
        // TreeExiting fires and asks for its own registration back.
        var scope = new ServiceScope(ServiceLifetime.Session);
        var old = new Clock { Name = "old" };
        var current = new Clock { Name = "current" };

        scope.Register(old);
        scope.Unregister(old);
        Assert.Equal(0, scope.Count);

        scope.Register(current);
        scope.Unregister(old); // the dead body's late teardown

        Assert.Equal(1, scope.Count);
        Assert.Same(current, ServiceLocator.ResolveFrom(Chain(null, scope, null), typeof(Clock)));
    }

    [Fact]
    public void ReRegisteringTheSameInstanceIsNotADuplicate()
    {
        var scope = new ServiceScope(ServiceLifetime.Session);
        var clock = new Clock();
        scope.Register(clock);
        scope.Register(clock);

        Assert.Equal(1, scope.Count);
    }

    [Fact]
    public void AnEmptyChainResolvesToNull()
    {
        Assert.Null(ServiceLocator.ResolveFrom(Chain(null, null, null), typeof(Clock)));
    }

    [Fact]
    public void LifetimeOrderIsTheOwnershipOrder()
    {
        // ResolveFrom walks the array backwards, so the enum's numbering IS the shadowing rule.
        // If someone renumbers these, a world service would stop shadowing a session one.
        Assert.Equal(0, (int)ServiceLifetime.Application);
        Assert.Equal(1, (int)ServiceLifetime.Session);
        Assert.Equal(2, (int)ServiceLifetime.World);
    }
}
