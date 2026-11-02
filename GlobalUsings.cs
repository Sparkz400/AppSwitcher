global using System;
global using System.Collections.Generic;
global using System.Collections.ObjectModel;
global using System.Diagnostics;
global using System.Globalization;
global using System.IO;
global using System.Linq;
global using System.Text.Json;
global using System.Text.Json.Serialization;
global using System.Threading.Tasks;
global using System.Windows;
global using System.Windows.Controls;
global using System.Windows.Data;
global using System.Windows.Input;
global using System.Windows.Media;
global using System.Windows.Threading;
global using CommunityToolkit.Mvvm.ComponentModel;
global using CommunityToolkit.Mvvm.Input;
global using Microsoft.Extensions.DependencyInjection;
global using Microsoft.Extensions.Logging;

global using AppSwitcher.Models;
global using AppSwitcher.Services;
global using AppSwitcher.ViewModels;
global using AppSwitcher.Converters;
global using AppSwitcher.Controls;

global using Point = System.Windows.Point;
global using Color = System.Windows.Media.Color;
global using Size = System.Windows.Size;
global using Application = System.Windows.Application;

global using ColorConverter = System.Windows.Media.ColorConverter;