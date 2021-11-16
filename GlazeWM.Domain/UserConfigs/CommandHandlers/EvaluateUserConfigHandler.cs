﻿using GlazeWM.Domain.UserConfigs.Commands;
using GlazeWM.Domain.Workspaces.Commands;
using GlazeWM.Infrastructure.Bussing;
using GlazeWM.Infrastructure.WindowsApi;
using GlazeWM.Infrastructure.Yaml;
using System;
using System.IO;
using System.Text.RegularExpressions;

namespace GlazeWM.Domain.UserConfigs.CommandHandlers
{
  class EvaluateUserConfigHandler : ICommandHandler<EvaluateUserConfigCommand>
  {
    private Bus _bus;
    private UserConfigService _userConfigService;
    private KeybindingService _keybindingService;
    private YamlDeserializationService _yamlDeserializationService;

    public EvaluateUserConfigHandler(Bus bus, UserConfigService userConfigService, KeybindingService keybindingService, YamlDeserializationService yamlDeserializationService)
    {
      _bus = bus;
      _userConfigService = userConfigService;
      _keybindingService = keybindingService;
      _yamlDeserializationService = yamlDeserializationService;
    }

    public CommandResponse Handle(EvaluateUserConfigCommand command)
    {
      UserConfig deserializedConfig = null;

      try
      {
        var userConfigPath = _userConfigService.UserConfigPath;

        if (!File.Exists(userConfigPath))
        {
          // Initialize the user config with the sample config.
          Directory.CreateDirectory(Path.GetDirectoryName(userConfigPath));
          File.Copy(_userConfigService.SampleUserConfigPath, userConfigPath, false);
        }

        deserializedConfig = DeserializeUserConfig(userConfigPath);
      }
      catch (Exception exception)
      {
        var errorMessage = FormatErrorMessage(exception);
        throw new FatalUserException(errorMessage);
      }

      // Create an inactive `Workspace` for each workspace config.
      foreach (var workspaceConfig in deserializedConfig.Workspaces)
        _bus.Invoke(new CreateWorkspaceCommand(workspaceConfig.Name));

      // Register keybindings.
      _bus.Invoke(new RegisterKeybindingsCommand(deserializedConfig.Keybindings));

      // Merge default window rules with user-defined rules.
      var defaultWindowRules = _userConfigService.DefaultWindowRules;
      deserializedConfig.WindowRules.InsertRange(0, defaultWindowRules);

      _userConfigService.UserConfig = deserializedConfig;

      return CommandResponse.Ok;
    }

    private UserConfig DeserializeUserConfig(string userConfigPath)
    {
      var userConfigLines = File.ReadAllLines(userConfigPath);
      var input = new StringReader(string.Join(Environment.NewLine, userConfigLines));

      return _yamlDeserializationService.Deserialize<UserConfig>(input);
    }

    private string FormatErrorMessage(Exception exception)
    {
      var errorMessage = exception.Message;

      if (exception.InnerException?.Message != null)
      {
        var unknownPropertyRegex = new Regex(@"Property '(?<property>.*?)' not found on type");
        var match = unknownPropertyRegex.Match(exception.InnerException.Message);

        // Improve error message shown in case of unknown property error.
        if (match.Success)
          errorMessage = $"Unknown property in config: {match.Groups["property"]}.";
        else
          errorMessage += $". {exception.InnerException.Message}";
      }

      return errorMessage;
    }
  }
}
