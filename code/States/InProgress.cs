using Sandbox;
using System.Collections.Generic;

namespace TTT;

public partial class InProgress : BaseState
{
	public List<Player> AlivePlayers { get; set; }
	public List<Player> Spectators { get; set; }

	public Player[] Innocents { get; set; }
	public Player[] Detectives { get; set; }
	public Player[] Traitors { get; set; }

	/// <summary>
	/// Unique case where InProgress has a seperate fake timer for Innocents.
	/// The real timer is only displayed to Traitors as it increments every player death during the round.
	/// </summary>
	[Net]
	public TimeUntil FakeTime { get; private set; }
	public string FakeTimeFormatted => FakeTime.Relative.TimerString();

	public override string Name => "In Progress";
	public override int Duration => Game.InProgressTime;

	private int InnocentTeamDeathCount { get; set; }
	private readonly List<RoleButton> _logicButtons = new();

	public override void OnPlayerKilled( Player player )
	{
		base.OnPlayerKilled( player );

		TimeLeft += Game.InProgressSecondsPerDeath;

		if ( player.Team == Team.Innocents )
			InnocentTeamDeathCount += 1;

		float percentDead = (float)InnocentTeamDeathCount / (Innocents.Length + Detectives.Length);
		if ( percentDead >= Game.CreditsAwardPercentage )
		{
			GivePlayersCredits( new Traitor(), Game.CreditsAwarded );
			InnocentTeamDeathCount = 0;
		}

		if ( player.Role is Traitor )
			GivePlayersCredits( new Detective(), Game.DetectiveTraitorDeathReward );
		else if ( player.Role is Detective && player.LastAttacker is Player p && p.IsAlive() && p.Team == Team.Traitors )
			GiveTraitorCredits( p );

		AlivePlayers.Remove( player );
		Spectators.Add( player );

		Karma.OnPlayerKilled( player );
		Scoring.OnPlayerKilled( player );

		player.UpdateMissingInAction();
		ChangeRoundIfOver();
	}

	public override void OnPlayerJoin( Player player )
	{
		base.OnPlayerJoin( player );

		Spectators.Add( player );
		SyncPlayer( player );
	}

	public override void OnPlayerLeave( Player player )
	{
		base.OnPlayerLeave( player );

		AlivePlayers.Remove( player );
		Spectators.Remove( player );

		ChangeRoundIfOver();
	}

	protected override void OnStart()
	{
		base.OnStart();

		Event.Run( TTTEvent.Round.RolesAssigned );

		if ( !Host.IsServer )
			return;

		FakeTime = TimeLeft;

		// For now, if the RandomWeaponCount of the map is zero, let's just give the players
		// a fixed weapon loadout.
		if ( MapHandler.RandomWeaponCount == 0 )
		{
			foreach ( var player in AlivePlayers )
			{
				GiveFixedLoadout( player );
			}
		}

		foreach ( var ent in Entity.All )
		{
			if ( ent is RoleButton button )
				_logicButtons.Add( button );
			else if ( ent is Corpse corpse )
				corpse.Delete();
		}
	}

	private void GiveFixedLoadout( Player player )
	{
		if ( player.Inventory.Add( new MP5() ) )
			player.GiveAmmo( AmmoType.PistolSMG, 120 );

		if ( player.Inventory.Add( new Revolver() ) )
			player.GiveAmmo( AmmoType.Magnum, 20 );
	}

	protected override void OnTimeUp()
	{
		base.OnTimeUp();

		LoadPostRound( Team.Innocents, WinType.TimeUp );
	}

	private Team IsRoundOver()
	{
		List<Team> aliveTeams = new();

		foreach ( var player in AlivePlayers )
		{
			if ( !aliveTeams.Contains( player.Team ) )
				aliveTeams.Add( player.Team );
		}

		if ( aliveTeams.Count == 0 )
			return Team.None;

		return aliveTeams.Count == 1 ? aliveTeams[0] : Team.None;
	}

	public void LoadPostRound( Team winningTeam, WinType winType )
	{
		Game.Current.ForceStateChange( new PostRound( winningTeam, winType ) );

		UI.GeneralMenu.LoadPlayerData( Innocents, Detectives, Traitors );
	}

	public override void OnSecond()
	{
		if ( !Host.IsServer )
			return;

		if ( Game.PreventWin )
			TimeLeft += 1f;

		if ( TimeLeft )
			OnTimeUp();

		_logicButtons.ForEach( x => x.OnSecond() ); // Tick role button delay timer.

		if ( !Utils.HasMinimumPlayers() && IsRoundOver() == Team.None )
			Game.Current.ForceStateChange( new WaitingState() );
	}

	private bool ChangeRoundIfOver()
	{
		var result = IsRoundOver();

		if ( result != Team.None && !Game.PreventWin )
		{
			LoadPostRound( result, WinType.Elimination );
			return true;
		}

		return false;
	}

	private void GivePlayersCredits( BaseRole role, int credits )
	{
		var clients = Utils.GetAliveClientsWithRole( role );

		clients.ForEach( ( cl ) =>
		{
			if ( cl.Pawn is Player p )
				p.Credits += credits;
		} );

		UI.InfoFeed.DisplayRoleEntry
		(
			To.Multiple( clients ),
			Asset.GetInfo<RoleInfo>( role.Title ),
			$"You have been awarded {credits} credits for your performance."
		);
	}

	private void GiveTraitorCredits( Player traitor )
	{
		traitor.Credits += Game.TraitorDetectiveKillReward;
		UI.InfoFeed.DisplayClientEntry( To.Single( traitor.Client ), $"have received {Game.TraitorDetectiveKillReward} credits for killing a Detective" );
	}

	[TTTEvent.Player.RoleChanged]
	private static void OnPlayerRoleChange( Player player, BaseRole oldRole )
	{
		if ( Host.IsClient )
			return;

		if ( Game.Current.State is InProgress inProgress )
			inProgress.ChangeRoundIfOver();
	}
}