using Sandbox;

namespace TTT;

[GameResource( "Perk", "perk", "TTT perk template.", Icon = "admin_panel_settings" )]
public class PerkInfo : ItemInfo { }

public abstract class Perk : EntityComponent<Player>
{
	public virtual string SlotText => string.Empty;
	public PerkInfo Info { get; private set; }

	public Perk()
	{
		Info = GameResource.GetInfo<PerkInfo>( GetType() );
	}

	public virtual void Simulate( Client client ) { }

#if SANDBOX && DEBUG
	[Event.Hotload]
	private void OnHotload()
	{
		Info = GameResource.GetInfo<PerkInfo>( GetType() );
	}
#endif
}
