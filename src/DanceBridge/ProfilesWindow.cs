using DanceBridge.Core;
using System.Windows;
using System.Windows.Controls;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
using ListBox = System.Windows.Controls.ListBox;
namespace DanceBridge;

public sealed class ProfilesWindow : Window
{
    readonly ProfileLibrary library;
    readonly ProfileStore store;
    public ProfileLibrary Result => library;
    readonly ListBox list = new() { DisplayMemberPath = "Name", MinHeight = 200 };
    readonly TextBox name = new();
    readonly TextBlock message = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) };
    public ProfilesWindow(ProfileLibrary library, ProfileStore store)
    {
        this.library = new ProfileLibrary{ActiveId=library.ActiveId,Profiles=library.Profiles.Select(ProfileStore.Clone).ToList()}; this.store = store;
        library=this.library;
        Title = "DanceBridge · Profiles"; Width = 620; Height = 600; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "Page"); SetResourceReference(ForegroundProperty, "Text");
        var root = new StackPanel { Margin = new Thickness(26) }; Content = new ScrollViewer{Content=root,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        root.Children.Add(new TextBlock { Text = "Profile library", FontSize = 24, Margin = new Thickness(0, 0, 0, 16) });
        list.SetResourceReference(BackgroundProperty, "Control"); list.SetResourceReference(ForegroundProperty, "Text");
        root.Children.Add(list); root.Children.Add(name);
        list.SelectionChanged += (_, _) => { if (list.SelectedItem is Profile selected) name.Text = selected.Name; };
        var actions = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) }; root.Children.Add(actions);
        Add("New", () => { var created = new Profile { Name = Unique("New profile") }; library.Profiles.Add(created); Reload(created.Id); });
        Add("Duplicate", () => { if (Selected is not { } original) return; var copy = ProfileStore.Clone(original); copy.Id = Guid.NewGuid(); copy.Name = Unique(original.Name + " copy"); library.Profiles.Add(copy); Reload(copy.Id); });
        Add("Rename", () => { if (Selected is not { } selected) return; var proposed = name.Text.Trim(); if (proposed.Length is 0 or > 80) throw new FormatException("Use 1–80 characters for the profile name."); if (library.Profiles.Any(p => p.Id != selected.Id && p.Name.Equals(proposed, StringComparison.OrdinalIgnoreCase))) throw new FormatException("That profile name already exists."); selected.Name = proposed; Reload(selected.Id); });
        Add("Restore defaults", () => { if (Selected is not { } selected || System.Windows.MessageBox.Show(this, "Replace this profile’s calibration, mappings and player settings with defaults?", "Restore defaults", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return; library.Profiles[library.Profiles.IndexOf(selected)] = new Profile { Id = selected.Id, Name = selected.Name }; Reload(selected.Id); });
        Add("Delete", () => { if (Selected is not { } selected) return; if (library.Profiles.Count == 1) throw new InvalidOperationException("Keep at least one profile."); if (System.Windows.MessageBox.Show(this, $"Delete profile ‘{selected.Name}’?", "Delete profile", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return; library.Profiles.Remove(selected); if (library.ActiveId == selected.Id) library.ActiveId = library.Profiles[0].Id; Reload(library.ActiveId); });
        var use = new Button { Content = "Use selected profile", Margin = new Thickness(0, 16, 0, 0) };
        use.Click += (_, _) => { try { if (Selected is not { } selected) return; library.ActiveId = selected.Id; store.Save(library); DialogResult = true; } catch (Exception ex) { message.Text = ex.Message; } };
        root.Children.Add(use); root.Children.Add(message); Reload(library.ActiveId);
        void Add(string label, Action action) { var button = new Button { Content = label, Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(10) }; button.Click += (_, _) => { try { action(); message.Text = "Choose Use selected profile to save these library changes."; } catch (Exception ex) { message.Text = ex.Message; } }; actions.Children.Add(button); }
    }
    Profile? Selected => list.SelectedItem as Profile;
    string Unique(string basis) { var result = basis; int suffix = 2; while (library.Profiles.Any(p => p.Name.Equals(result, StringComparison.OrdinalIgnoreCase))) result = basis + " " + suffix++; return result; }
    void Reload(Guid id) { list.ItemsSource = null; list.ItemsSource = library.Profiles; list.SelectedItem = library.Profiles.First(p => p.Id == id); }
}
