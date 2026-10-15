using System.IO;
using System.Text.Json;
using System.Windows.Media;
namespace DanceBridge;
public enum AppTheme { Dark, Light, Ddr }
public static class ThemeManager {
 public static AppTheme Current { get; private set; }
 static string SettingsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DanceBridge", "appearance.json");
 public static void Load(){var theme=AppTheme.Dark;try{if(File.Exists(SettingsPath))theme=JsonSerializer.Deserialize<AppTheme>(File.ReadAllText(SettingsPath));}catch(Exception ex) when(ex is IOException or JsonException or UnauthorizedAccessException){}Apply(Enum.IsDefined(theme)?theme:AppTheme.Dark);}
 public static void Save(){Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);var temp=SettingsPath+".tmp";File.WriteAllText(temp,JsonSerializer.Serialize(Current));File.Move(temp,SettingsPath,true);}
 public static SolidColorBrush Brush(string key)=>(SolidColorBrush)System.Windows.Application.Current.Resources[key];
 public static void Apply(AppTheme theme){Current=theme;bool light=theme==AppTheme.Light,ddr=theme==AppTheme.Ddr;
 var colors=new Dictionary<string,string>{
 ["Page"]=light?"#F3F3F1":ddr?"#102F63":"#161616",
 ["Sidebar"]=light?"#EAEAE7":ddr?"#0755A6":"#1C1C1C",
 ["Surface"]=light?"#FFFFFF":ddr?"#143E78":"#222222",
 ["Control"]=light?"#F7F7F5":ddr?"#151A23":"#1B1B1B",
 ["Button"]=light?"#F0F0ED":ddr?"#00757C":"#2D2D2D",
 ["Text"]=light?"#202020":"#EEEEEE",
 ["Muted"]=light?"#62625D":ddr?"#C9E5FF":"#AAAAA5",
 ["SecondaryText"]=light?"#50504C":ddr?"#E3F5FF":"#C4C4BF",
 ["Border"]=light?"#CECECA":ddr?"#4389C6":"#424242",
 ["Accent"]=light?"#252525":ddr?"#F5CE63":"#E0E0DA",
 ["TextOnDark"]="#FFFFFF",
 ["AccentText"]=light?"#FFFFFF":"#161616",
 ["Selection"]=light?"#DDDDD7":ddr?"#FFE45C":"#3B3B38",
 ["SelectionBorder"]=light?"#73736A":ddr?"#F5CE63":"#B9B9AF",
 ["Tile"]=light?"#F0F0EC":ddr?"#252E3C":"#303030",
 ["TileText"]=light?"#343430":"#DDDDD7",
 ["Scroll"]=light?"#AAAAA2":ddr?"#6CD9E7":"#666660",
 ["BadgeBackground"]=light?"#E8E8E2":ddr?"#FFE45C":"#353532",
 ["BadgeText"]=light?"#484842":ddr?"#10284C":"#D5D5CC",
 ["LiveBackground"]=light?"#D5EBDC":"#263E2E",
 ["LiveText"]=light?"#205431":"#B2DABD",
 ["Decorative"]=light?"#77776D":ddr?"#B4D9FA":"#88887E",
 ["DdrBlue"]="#48D4F2",["DdrPink"]="#FF80B5",["DdrOrange"]="#FFB36B",["DdrMint"]="#70E0BA",["DdrViolet"]="#C5ACFF",["DdrAmber"]="#F5D06C",["DdrLime"]="#B7E66A",["DdrCoral"]="#FF9B8D",["DdrInk"]="#10284C"};
 colors["PressedFill"]=light?"#202020":"#FFFFFF";
 colors["PressedInk"]=light?"#FFFFFF":"#101010";
 colors["MatSurface"]=ddr?"#14559D":colors["Surface"];
 colors["MappingSurface"]=ddr?"#163F7A":colors["Surface"];
 colors["StatusSurface"]=ddr?"#006B80":colors["Surface"];
 colors["FooterSurface"]=ddr?"#075E63":colors["Surface"];
 colors["Heading"]=ddr?"#FFE45C":colors["Text"];
 foreach(var (key,color) in colors){var brush=new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color));brush.Freeze();System.Windows.Application.Current.Resources[key]=brush;}
 }
}
