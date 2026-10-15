using DanceBridge.Core;
using System.Windows;
using System.Windows.Controls;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
using CheckBox = System.Windows.Controls.CheckBox;
namespace DanceBridge;

public sealed class PlayerSettingsWindow : Window
{
    public Profile Result { get; }
    public PlayerSettingsWindow(Profile profile)
    {
        Result = profile; Title = "DanceBridge · Players and game"; Width = 700; Height = 680; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty,"Page");SetResourceReference(ForegroundProperty,"Text");
        var root=new StackPanel{Margin=new Thickness(26)};Content=new ScrollViewer{Content=root,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        root.Children.Add(new TextBlock{Text="Players and output destination",FontSize=24,Margin=new Thickness(0,0,0,18)});
        var warning=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,12)};
        for(int p=0;p<4;p++)
        {
            int index=p;var row=new Grid{Margin=new Thickness(0,0,0,12)};row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(120)});row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(170)});
            var enabled=new CheckBox{Content="Port "+(p+1),IsChecked=profile.EnabledPorts[p],VerticalAlignment=VerticalAlignment.Center};enabled.SetResourceReference(ForegroundProperty,"Text");row.Children.Add(enabled);
            var name=new TextBox{Text=profile.PlayerNames[p],MaxLength=40,Margin=new Thickness(0,0,12,0)};Grid.SetColumn(name,1);row.Children.Add(name);
            var preset=new Button{Content="Player "+(p+1)+" preset"};Grid.SetColumn(preset,2);row.Children.Add(preset);
            enabled.Checked+=(_,_)=>{profile.EnabledPorts[index]=true;UpdateWarning();};enabled.Unchecked+=(_,_)=>{profile.EnabledPorts[index]=false;UpdateWarning();};
            name.TextChanged+=(_,_)=>profile.PlayerNames[index]=name.Text.Trim();preset.Click+=(_,_)=>{profile.ApplyPlayerPreset(index);UpdateWarning();};root.Children.Add(row);
        }
        root.Children.Add(warning);UpdateWarning();
        root.Children.Add(new TextBlock{Text="Output destination",FontSize=18,Margin=new Thickness(0,8,0,10)});
        var game=new TextBlock{Text=profile.GamePath??"All applications",TextWrapping=TextWrapping.Wrap};root.Children.Add(game);
        var actions=new WrapPanel{Margin=new Thickness(0,12,0,12)};root.Children.Add(actions);
        var choose=new Button{Content="Choose game .exe",Margin=new Thickness(0,0,8,0)};actions.Children.Add(choose);
        choose.Click+=(_,_)=>{var picker=new Microsoft.Win32.OpenFileDialog{Filter="Game executable|*.exe"};if(picker.ShowDialog(this)==true){profile.GamePath=picker.FileName;game.Text=picker.FileName;}};
        var all=new Button{Content="All applications"};actions.Children.Add(all);all.Click+=(_,_)=>{profile.GamePath=null;game.Text="All applications";};
        root.Children.Add(new TextBlock{Text="With a game selected, keys are released when you switch away. Release the mat before output resumes in the game.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,16)});
        var apply=new Button{Content="Apply settings"};root.Children.Add(apply);apply.Click+=(_,_)=>{try{profile.Validate();DialogResult=true;}catch(Exception ex){warning.Text=ex.Message;}};
        void UpdateWarning(){var conflicts=profile.DirectionConflicts();warning.Text=conflicts.Count==0?"Direction mappings are separate across enabled players.":"Shared direction keys: "+string.Join("; ",conflicts);}
    }
}
