using Sandbox;

namespace TTT;

public enum SomeState
{
	Alive,
	MissingInAction,
	ConfirmedDead,
	Spectator
}

public partial class Player
{
	public Corpse Corpse { get; set; }

	/// <summary>
	/// The player who confirmed this player's corpse.
	/// </summary>
	public Player Confirmer { get; private set; }
	public SomeState SomeState { get; set; } = SomeState.Spectator;
	public bool IsMissingInAction => SomeState == SomeState.MissingInAction;
	public bool IsConfirmedDead => SomeState == SomeState.ConfirmedDead;
	public bool IsRoleKnown { get; set; }
	public string LastSeenPlayerName { get; set; }

	public void RemoveCorpse()
	{
		Host.AssertServer();

		if ( !Corpse.IsValid() )
			return;

		Corpse.Delete();
		Corpse = null;
	}

	private void BecomeCorpse()
	{
		Host.AssertServer();

		Corpse = new Corpse( this );
	}

	public void UpdateMissingInAction( Player player = null )
	{
		Host.AssertServer();

		if ( player is not null )
		{
			SetSomeState( To.Single( player ), SomeState.MissingInAction );
			return;
		}

		SomeState = SomeState.MissingInAction;
		SetSomeState( Team.Traitors.ToClients(), SomeState.MissingInAction );

		if ( Team != Team.Traitors )
			SetSomeState( To.Single( this ), SomeState.MissingInAction );
	}

	public void Confirm( To to, Player confirmer = null )
	{
		Host.AssertServer();

		var wasPreviouslyConfirmed = true;

		if ( !IsConfirmedDead )
		{
			Confirmer = confirmer;
			SomeState = SomeState.ConfirmedDead;
			IsRoleKnown = true;
			wasPreviouslyConfirmed = false;
		}

		if ( Corpse.IsValid() )
			Corpse.SendPlayer( to );

		SendRole( to );
		ClientConfirm( to, Confirmer, wasPreviouslyConfirmed );
	}

	private void CheckLastSeenPlayer()
	{
		if ( HoveredEntity is Player player && player.CanHint( this ) )
			LastSeenPlayerName = player.Client.Name;
	}

	private void ResetConfirmationData()
	{
		Confirmer = null;
		Corpse = null;
		LastSeenPlayerName = string.Empty;
		IsRoleKnown = false;
	}

	[ClientRpc]
	private void ClientConfirm( Player confirmer, bool wasPreviouslyConfirmed = false )
	{
		Confirmer = confirmer;
		SomeState = SomeState.ConfirmedDead;

		if ( wasPreviouslyConfirmed || !Confirmer.IsValid() || !Corpse.IsValid() )
			return;

		Event.Run( TTTEvent.Player.CorpseFound, this );
	}

	[ClientRpc]
	public void SetSomeState( SomeState someState )
	{
		SomeState = someState;
	}

	[TTTEvent.Game.ClientJoined]
	private void SyncClient( Client client )
	{
		if ( this.IsAlive() )
			SetSomeState( To.Single( client ), SomeState.Alive );

		if ( IsConfirmedDead )
			Confirm( To.Single( client ) );
		else if ( IsRoleKnown )
			SendRole( To.Single( client ) );
	}
}
