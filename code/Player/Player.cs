using Sandbox;

namespace TTT;

[Title( "Player" ), Icon( "emoji_people" )]
public partial class Player : AnimatedEntity
{
	[Net, Predicted]
	public PlayerModel PlayerModel { get; set; }
	[Net, Predicted]
	public PlayerAnimator Animator { get; private set; }
	[Net]
	public Inventory Inventory { get; private set; }
	public Perks Perks { get; private init; }


	public CameraMode Camera
	{
		get => Components.Get<CameraMode>();
		set
		{
			var current = Camera;
			if ( current == value )
				return;

			Components.RemoveAny<CameraMode>();
			Components.Add( value );
		}
	}

	/// <summary>
	/// The player earns score by killing players on opposite teams, confirming bodies
	/// or surviving the round.
	/// </summary>
	public int Score
	{
		get => Client.GetInt( Strings.Score );
		set => Client.SetInt( Strings.Score, value );
	}

	/// <summary>
	/// The score gained during a single round. This gets added to the actual score
	/// at the end of a round.
	/// </summary>
	public int RoundScore { get; set; }

	public const float DropVelocity = 300;

	public Player()
	{
		Perks = new( this );
	}

	public override void Spawn()
	{
		base.Spawn();

		Tags.Add( "player" );
		Tags.Add( "solid" );

		Role = new NoneRole();

		Health = 0;
		LifeState = LifeState.Respawnable;
		Transmit = TransmitType.Always;

		EnableAllCollisions = false;
		EnableDrawing = false;
		EnableHideInFirstPerson = true;
		EnableLagCompensation = true;
		EnableShadowInFirstPerson = true;
		EnableTouch = false;

		Inventory = new(this);
		PlayerModel = new(this);
		Animator = PlayerModel.Animator;

		Camera = new FreeSpectateCamera();
	}

	public override void ClientSpawn()
	{
		base.ClientSpawn();

		Role = new NoneRole();
	}

	public void Respawn()
	{
		Host.AssertServer();

		LifeState = LifeState.Respawnable;

		DeleteFlashlight();
		DeleteItems();
		ResetConfirmationData();
		ResetDamageData();
		Client.SetValue( Strings.Spectator, IsForcedSpectator );
		Role = new NoneRole();

		Velocity = Vector3.Zero;
		WaterLevel = 0;
		Credits = 0;

		if ( !IsForcedSpectator )
		{
			Health = MaxHealth;
			Status = PlayerStatus.Alive;
			UpdateStatus( To.Everyone );
			LifeState = LifeState.Alive;

			EnableAllCollisions = true;
			PlayerModel.EnableDrawing = true;
			EnableTouch = true;

			Controller = new PlayerController()
			{
				AirAcceleration = 65f, //1500f,
				WalkSpeed = 110f,
				DefaultSpeed = 230f,
				StopSpeed = 105f,
				Acceleration = 8f,
				GroundFriction = 5,
				AutoJump = false,
				// Matches HL2 according to 'test/scale` map.
				JumpSpeed = 180f,
				Gravity = 800f,
			};

			Camera = new FirstPersonCamera();

			CreateHull();
			CreateFlashlight();
			DressPlayer();
			ResetInterpolation();

			Event.Run( GameEvent.Player.Spawned, this );
			Game.Current.State.OnPlayerSpawned( this );
		}
		else
		{
			Status = PlayerStatus.Spectator;
			UpdateStatus( To.Everyone );
			MakeSpectator( false );
		}

		ClientRespawn( this );
	}

	private void ClientRespawn()
	{
		Host.AssertClient();

		DeleteFlashlight();
		ResetConfirmationData();
		ResetDamageData();

		if ( !IsLocalPawn )
			Role = new NoneRole();
		else
			ClearButtons();

		if ( IsSpectator )
			return;

		CreateFlashlight();
		Event.Run( GameEvent.Player.Spawned, this );
	}

	public override void Simulate( Client client )
	{
		var controller = GetActiveController();
		controller?.Simulate( client, this, Animator );


		if ( Input.ActiveChild is Carriable carriable )
			Inventory.SetActive( carriable );

		SimulateActiveChild( client, ActiveChild );

		if ( this.IsAlive() )
		{
			SimulateFlashlight();
			SimulatePerks();
		}

		if ( IsClient )
		{
			ActivateRoleButton();
		}
		else
		{
			CheckAFK();

			if ( !this.IsAlive() )
			{
				ChangeSpectateCamera();
				return;
			}

			PlayerUse();
			CheckLastSeenPlayer();
			CheckPlayerDropCarriable();
		}
	}

	public override void FrameSimulate( Client client )
	{
		var controller = GetActiveController();
		controller?.FrameSimulate( client, this, Animator );

		if ( WaterLevel > 0.9f )
		{
			Audio.SetEffect( "underwater", 1, velocity: 5.0f );
		}
		else
		{
			Audio.SetEffect( "underwater", 0, velocity: 1.0f );
		}

		DisplayEntityHints();
		ActiveChild?.FrameSimulate( client );
	}

	/// <summary>
	/// Called after the camera setup logic has run. Allow the player to
	/// do stuff to the camera, or using the camera. Such as positioning entities
	/// relative to it, like viewmodels etc.
	/// </summary>
	public override void PostCameraSetup( ref CameraSetup setup )
	{
		ActiveChild?.PostCameraSetup( ref setup );
	}

	/// <summary>
	/// Called from the gamemode, clientside only.
	/// </summary>
	public override void BuildInput( InputBuilder input )
	{
		if ( input.StopProcessing )
			return;

		ActiveChild?.BuildInput( input );

		if ( input.StopProcessing )
			return;

		GetActiveController()?.BuildInput( input );

		if ( input.StopProcessing )
			return;

		Animator.BuildInput( input );
	}

	public void RenderHud( Vector2 screenSize )
	{
		if ( !this.IsAlive() )
			return;

		ActiveChild?.RenderHud( screenSize );
		UI.Crosshair.Instance?.RenderCrosshair( screenSize * 0.5, ActiveChild );
	}

	#region Controller
	[Net, Predicted]
	public PlayerController Controller { get; set; }

	[Net, Predicted]
	public PawnController DevController { get; set; }

	public PawnController GetActiveController()
	{
		return Controller;
	}
	#endregion

	public void CreateHull()
	{
		SetupPhysicsFromAABB( PhysicsMotionType.Keyframed, new Vector3( -16, -16, 0 ), new Vector3( 16, 16, 72 ) );
		PlayerModel.EnableHitboxes = true;
	}

	public override void StartTouch( Entity other )
	{
		if ( !IsServer )
			return;

		switch ( other )
		{
			case Ammo ammo:
			{
				ammo.StartTouch( this );
				break;
			}
			case Carriable carriable:
			{
				Inventory.Pickup( carriable );
				break;
			}
		}
	}

	public void DeleteItems()
	{
		ClearAmmo();
		Inventory.DeleteContents();
		Perks.DeleteContents();
		RemoveAllClothing();
	}

	#region ActiveChild
	[Net, Predicted]
	public Carriable ActiveChild { get; set; }

	public Carriable LastActiveChild { get; set; }

	public void SimulateActiveChild( Client client, Carriable child )
	{
		if ( LastActiveChild != child )
		{
			OnActiveChildChanged( LastActiveChild, child );
			LastActiveChild = child;
		}

		if ( !LastActiveChild.IsValid() )
			return;

		if ( !LastActiveChild.IsAuthority )
			return;

		if ( LastActiveChild.TimeSinceDeployed > LastActiveChild.Info.DeployTime )
			LastActiveChild.Simulate( client );
	}

	public void OnActiveChildChanged( Carriable previous, Carriable next )
	{
		previous?.ActiveEnd( this, previous.Owner != this );
		next?.ActiveStart( this );
	}

	private void CheckPlayerDropCarriable()
	{
		if ( Input.Pressed( InputButton.Drop ) && !Input.Down( InputButton.Run ) )
		{
			var droppedEntity = Inventory.DropActive();

			if ( droppedEntity is not null )
			{
				if ( droppedEntity.PhysicsGroup is not null )
				{
					droppedEntity.PhysicsGroup.Velocity = Velocity + (EyeRotation.Forward + EyeRotation.Up) * DropVelocity;
				}
			}
		}
	}
	#endregion

	private void SimulatePerks()
	{
		foreach ( var perk in Perks )
		{
			perk.Simulate( Client );
		}
	}

	public override void OnChildAdded( Entity child )
	{
		switch ( child )
		{
			case Clothing clothing:
			{
				Clothes.Add( clothing );
				break;
			}
		}
	}

	public override void OnChildRemoved( Entity child )
	{
		switch ( child )
		{
			case Carriable carriable:
			{
				Inventory.OnChildRemoved( carriable );
				break;
			}
			case Clothing clothing:
			{
				Clothes.Remove( clothing );
				break;
			}
		}
	}

	protected override void OnComponentAdded( EntityComponent component )
	{
		Perks?.OnComponentAdded( component );
	}

	protected override void OnComponentRemoved( EntityComponent component )
	{
		Perks?.OnComponentRemoved( component );
	}

	protected override void OnDestroy()
	{
		if ( IsServer )
			RemoveCorpse();

		DeleteFlashlight();

		base.OnDestroy();
	}

	[ClientRpc]
	public static void ClientRespawn( Player player )
	{
		if ( !player.IsValid() )
			return;

		player.ClientRespawn();
	}
}
