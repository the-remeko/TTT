using Sandbox;
using System.Collections;
using System.Collections.Generic;

namespace TTT;

/// <summary>
/// A sublist of <see cref="Entity.Children"/> that contains entities 
/// of type <see cref="Carriable"/>.
/// </summary>
public sealed partial class Inventory : BaseNetworkable, IEnumerable<Carriable>
{
	[Net]
	public Player Owner { get; private set; }

	public Carriable Active
	{
		get => Owner.ActiveChild;
		private set => Owner.ActiveChild = value;
	}

	public Carriable this[int i] => _list[i];

	public int Count => _list.Count;

	[Net]
	private IList<Carriable> _list { get; set; }

	private readonly int[] _slotCapacity = new int[] { 1, 1, 1, 3, 3, 1 };
	private readonly int[] _weaponsOfAmmoType = new int[] { 0, 0, 0, 0, 0, 0 };

	private const float DropPositionOffset = 3f;
	private const float DropVelocity = 500f;

	public Inventory() {}

	public Inventory( Player player ) => Owner = player;

	public bool Add( Carriable carriable, bool makeActive = false )
	{
		Host.AssertServer();

		if ( !carriable.IsValid() )
			return false;

		if ( carriable.Owner is not null )
			return false;

		if ( !CanAdd( carriable ) )
			return false;

		carriable.SetParent( Owner.PlayerModel, true );

		_list.Add(carriable);

		carriable.OnCarryStart( Owner );

		_slotCapacity[(int)carriable.Info.Slot] -= 1;

		if ( carriable is Weapon weapon )
			_weaponsOfAmmoType[(int)weapon.Info.AmmoType] += 1;

		if ( makeActive )
			SetActive( carriable );

		return true;
	}

	public bool CanAdd( Carriable carriable )
	{
		// Used to check carriable.Parent == Owner here, might need to add a similar check
		if ( Host.IsClient )
			return true;

		if ( !HasFreeSlot( carriable.Info.Slot ) )
			return false;

		if ( !carriable.CanCarry( Owner ) )
			return false;

		return true;
	}

	public bool Contains( Carriable entity ) => _list.Contains( entity );

	public void Pickup( Carriable carriable )
	{
		if ( Add( carriable ) )
			Sound.FromEntity( Strings.WeaponPickupSound, Owner );
	}

	public bool HasFreeSlot( SlotType slotType )
	{
		return _slotCapacity[(int)slotType] > 0;
	}

	public bool HasWeaponOfAmmoType( AmmoType ammoType )
	{
		return ammoType != AmmoType.None && _weaponsOfAmmoType[(int)ammoType] > 0;
	}

	public void OnUse( Carriable carriable )
	{
		Host.AssertServer();

		if ( !carriable.CanCarry( Owner ) )
			return;

		if ( HasFreeSlot( carriable.Info.Slot ) )
		{
			Add( carriable );
			return;
		}

		var entities = new List<Carriable>();
		foreach(var entity in _list)
		{
			if(entity.Info.Slot == carriable.Info.Slot)
				entities.Add(entity);
		}

		if ( Active is not null && Active.Info.Slot == carriable.Info.Slot )
		{
			if ( DropActive() is not null )
				Add( carriable, true );
		}
		else if ( entities.Count == 1 )
		{
			if ( Drop( entities[0] ) )
				Add( carriable, false );
		}
	}

	public bool SetActive( Carriable carriable )
	{
		if ( Active == carriable )
			return false;

		if ( !Contains( carriable ) )
			return false;

		Active = carriable;
		return true;
	}

	public T Find<T>() where T : Carriable
	{
		foreach ( var carriable in _list )
		{
			if ( carriable is not T t || t.Equals( default( T ) ) )
				continue;

			return t;
		}

		return default;
	}

	public bool Drop( Carriable carriable )
	{
		if ( !Host.IsServer )
			return false;

		if ( !Contains( carriable ) )
			return false;

		if ( !carriable.Info.CanDrop )
			return false;

		OnChildRemoved(carriable);
		carriable.Parent = null;

		return true;
	}

	public Carriable DropActive()
	{
		if ( !Host.IsServer )
			return null;

		if ( Drop( Active ) )
		{
			var active = Active;
			Active = null;
			return active;
		}

		return null;
	}

	public void DropAll()
	{
		Host.AssertServer();

		foreach ( var carriable in _list )
			Drop( carriable );

		Active = null;

		DeleteContents();
	}

	public void DeleteContents()
	{
		Host.AssertServer();

		foreach ( var carriable in _list )
			carriable.Delete();

		Active = null;

		_list.Clear();
	}

	public void OnChildAdded( Carriable carriable )
	{
		if ( !CanAdd( carriable ) )
			return;

		if ( _list.Contains( carriable ) )
			throw new System.Exception( "Trying to add to inventory multiple times. This is gated by Entity:OnChildAdded and should never happen!" );

		_list.Add( carriable );

		carriable.OnCarryStart( Owner );

		_slotCapacity[(int)carriable.Info.Slot] -= 1;

		if ( carriable is Weapon weapon )
			_weaponsOfAmmoType[(int)weapon.Info.AmmoType] += 1;
	}

	public void OnChildRemoved( Carriable carriable )
	{
		if ( !_list.Remove( carriable ) )
			return;

		carriable.OnCarryDrop( Owner );

		_slotCapacity[(int)carriable.Info.Slot] += 1;

		if ( carriable is Weapon weapon )
			_weaponsOfAmmoType[(int)weapon.Info.AmmoType] -= 1;
	}

	public T DropEntity<T>( Deployable<T> self ) where T : ModelEntity, new()
	{
		Host.AssertServer();

		var carriable = self as Carriable;
		if ( !carriable.IsValid() || !Contains( carriable ) )
			return null;

		carriable.Owner.Inventory.OnChildRemoved(carriable);
		carriable.Parent = null;
		carriable.Delete();

		var droppedEntity = new T
		{
			Owner = Owner,
			Position = Owner.EyePosition + Owner.EyeRotation.Forward * DropPositionOffset,
			Rotation = Owner.EyeRotation,
			Velocity = Owner.EyeRotation.Forward * DropVelocity,
			PhysicsEnabled = true
		};

		return droppedEntity;
	}

	public IEnumerator<Carriable> GetEnumerator() => _list.GetEnumerator();

	IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
