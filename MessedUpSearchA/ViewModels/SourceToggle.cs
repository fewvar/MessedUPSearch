using System;
using CommunityToolkit.Mvvm.ComponentModel;
using MessedUpSearchA.Services.Parsing;

namespace MessedUpSearchA.ViewModels;

public partial class SourceToggle : ObservableObject
{
    private readonly Func<IArtistSource> _factory;

    [ObservableProperty] private bool _isEnabled = true;

    public SourceToggle(string name, Func<IArtistSource> factory)
    {
        Name = name;
        _factory = factory;
    }

    public string Name { get; }

    public IArtistSource Create() => _factory();
}
