using Sandbox;
using System;
using System.Collections.Generic;

namespace TTT;

public class PreRound : BaseRound
{
	public override string RoundName => "Preparing";
	public override int RoundDuration => Game.Current.TotalRoundsPlayed == 0 ? Game.PreRoundTime * 2 : Game.PreRoundTime;

	public override void OnPlayerSpawned( Player player )
	{
		base.OnPlayerSpawned( player );

		Karma.Apply( player );
		player.Inventory.Add( new Hands() );
	}

	public override void OnPlayerJoin( Player player )
	{
		base.OnPlayerJoin( player );
		player.Respawn();
	}

	public override void OnPlayerKilled( Player player )
	{
		base.OnPlayerKilled( player );

		StartRespawnTimer( player );
	}

	protected override void OnStart()
	{
		base.OnStart();

		MapHandler.CleanUp();

		if ( !Host.IsServer )
			return;

		foreach ( var client in Client.All )
		{
			var player = client.Pawn as Player;
			player.Respawn();
		}
	}

	protected override void OnTimeUp()
	{
		base.OnTimeUp();

		List<Player> players = new();
		List<Player> spectators = new();

		foreach ( var client in Client.All )
		{
			var player = client.Pawn as Player;
			player.Client.SetValue( Strings.Spectator, player.IsForcedSpectator );

			if ( player.IsForcedSpectator )
			{
				player.MakeSpectator( false );
				spectators.Add( player );

				continue;
			}

			if ( player.IsAlive() )
				player.Health = player.MaxHealth;
			else
				player.Respawn();

			players.Add( player );
		}

		int traitorCount = Math.Max( players.Count >> 2, 1 );
		int detectiveCount = players.Count >> 3;
		players.Shuffle();

		List<Player> innocents, detectives, traitors;
		innocents = detectives = traitors = new();

		// TODO: Matt cleanup before merging...
		int index = 0;
		while ( traitorCount-- > 0 )
		{
			traitors.Add( players[index] );
			players[index++].Role = new Traitor();
		}

		while ( detectiveCount-- > 0 )
		{
			detectives.Add( players[index] );
			players[index++].Role = new Detective();
		}

		while ( index < players.Count )
		{
			innocents.Add( players[index] );
			players[index++].Role = new Innocent();
		}

		Game.Current.ChangeRound( new InProgressRound
		{
			AlivePlayers = players,
			Spectators = spectators,
			Innocents = innocents.ToArray(),
			Detectives = detectives.ToArray(),
			Traitors = traitors.ToArray()
		} );
	}

	private static async void StartRespawnTimer( Player player )
	{
		await GameTask.DelaySeconds( 1 );

		if ( player.IsValid() && Game.Current.Round is PreRound )
			player.Respawn();
	}
}
