using Godot;

public partial class CardController : Control
{
	private Label _TypeLabel;
	private Label _AttrLabel;
	private Label _NameLabel;

	public override void _Ready()
	{
		// 获取子节点引用
		_TypeLabel = GetNode<Label>("MarginContainer/PanelContainer/HBoxContainer/VBoxContainer/TypeLabel");
		_AttrLabel = GetNode<Label>("MarginContainer/PanelContainer/HBoxContainer/VBoxContainer/AttrLabel");
		_NameLabel = GetNode<Label>("MarginContainer/PanelContainer/HBoxContainer/NameLabel");

	}
	
	// 公共方法：设置文本
	public void SetType(string newText)
	{
		if (_TypeLabel != null)
			_TypeLabel.Text = newText;
	}

	public void SetAttr(string newText)
	{
		if (_AttrLabel != null)
			_AttrLabel.Text = newText;
	}

	public new void SetName(string newText)
	{
		if (_NameLabel != null)
			_NameLabel.Text = newText;
	}
}
