using DoomArchitect.Core.Configuration;
using Godot;

/// <summary>
/// The Test Map toolbar control: a play button that launches immediately at
/// the last skill/monsters choice, plus a small separate dropdown-arrow
/// button that opens a popup listing every skill twice (with/without
/// monsters). Picking one just updates the choice for next time - it does
/// not launch by itself, matching the real default behavior of the
/// equivalent split button this ports (its own "auto-launch on pick"
/// setting defaults off).
/// </summary>
public partial class TestMapToolbar : PanelContainer
{
	private const string ButtonsPath = "MarginContainer/HBoxContainer";

	public MapOverlay Overlay { get; set; }
	public MainMenuBar MainMenuBar { get; set; }

	private Button _testButton;
	private Button _dropdownButton;
	private PopupMenu _skillPopup;

	public override void _Ready()
	{
		_testButton = GetNode<Button>($"{ButtonsPath}/TestButton");
		_dropdownButton = GetNode<Button>($"{ButtonsPath}/TestDropdownButton");
		_skillPopup = GetNode<PopupMenu>("SkillPopup");

		// All three bundled game configs currently share the same skill
		// list, so Doom is used as a representative source for the popup's
		// labels regardless of which map is actually loaded. Item ids
		// sign-encode the choice (positive = with monsters, negative =
		// without, magnitude = skill number) - with-monsters entries listed
		// first, then a separator, then the no-monsters entries.
		var skills = GameConfigurations.Get(GameConfigurationKind.Doom).GetSkills();
		var withMonstersIcon = GD.Load<Texture2D>("res://Assets/Icons/skill_with_monsters.png");
		var noMonstersIcon = GD.Load<Texture2D>("res://Assets/Icons/skill_no_monsters.png");

		foreach (var skill in skills)
		{
			_skillPopup.AddIconItem(withMonstersIcon, skill.Title, skill.Number);
		}

		_skillPopup.AddSeparator();

		foreach (var skill in skills)
		{
			_skillPopup.AddIconItem(noMonstersIcon, $"{skill.Title} (No Monsters)", -skill.Number);
		}

		_testButton.Pressed += () => MainMenuBar?.TestMap();
		_dropdownButton.Pressed += ShowSkillPopup;
		_skillPopup.IdPressed += OnSkillPicked;
	}

	private void ShowSkillPopup()
	{
		var position = (Vector2I)_dropdownButton.GetScreenPosition() + new Vector2I(0, (int)_dropdownButton.Size.Y);
		_skillPopup.Popup(new Rect2I(position, Vector2I.Zero));
	}

	private void OnSkillPicked(long id)
	{
		if (Overlay == null) return;

		Overlay.LastTestSkill = Mathf.Abs((int)id);
		Overlay.LastTestNoMonsters = id < 0;
	}
}
