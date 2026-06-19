using Embervale.Core.Events;
using Embervale.Entities;

namespace Embervale.Items;

/// <summary>Raised whenever an entity equips or unequips something.</summary>
public readonly record struct EquipmentChangedEvent(IEntity Owner) : IGameEvent;
