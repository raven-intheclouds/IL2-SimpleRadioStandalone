﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using Ciribob.IL2.SimpleRadio.Standalone.Client.Network;
using Ciribob.IL2.SimpleRadio.Standalone.Client.Settings;
using Ciribob.IL2.SimpleRadio.Standalone.Common;
using Ciribob.IL2.SimpleRadio.Standalone.Client.Singletons;
using Ciribob.IL2.SimpleRadio.Standalone.Client.UI;
using Ciribob.IL2.SimpleRadio.Standalone.Client.Utils;
using NLog;
using SharpDX.DirectInput;

namespace Ciribob.IL2.SimpleRadio.Standalone.Client.Settings
{
    public class InputDeviceManager : IDisposable
    {
        public delegate void DetectButton(InputDevice inputDevice);

        public delegate void DetectPttCallback(List<InputBindState> buttonStates);

        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public static HashSet<Guid> _blacklistedDevices = new HashSet<Guid>
        {
            new Guid("1b171b1c-0000-0000-0000-504944564944"),
            //Corsair K65 Gaming keyboard  It reports as a Joystick when its a keyboard...
            new Guid("1b091b1c-0000-0000-0000-504944564944"), // Corsair K70R Gaming Keyboard
            new Guid("1b1e1b1c-0000-0000-0000-504944564944"), //Corsair Gaming Scimitar RGB Mouse
            new Guid("16a40951-0000-0000-0000-504944564944"), //HyperX 7.1 Audio
            new Guid("b660044f-0000-0000-0000-504944564944"), // T500 RS Gear Shift
            new Guid("00f2068e-0000-0000-0000-504944564944") //CH PRO PEDALS USB
        };

        //devices that report incorrectly but SHOULD work?
        public static HashSet<Guid> _whitelistDevices = new HashSet<Guid>
        {
            new Guid("1105231d-0000-0000-0000-504944564944"), //GTX Throttle
            new Guid("b351044f-0000-0000-0000-504944564944"), //F16 MFD 2 Usage: Generic Type: Supplemental
            new Guid("11401dd2-0000-0000-0000-504944564944"), //Leo Bodnar BUtton Box
            new Guid("204803eb-0000-0000-0000-504944564944"), // VPC Throttle
            new Guid("204303eb-0000-0000-0000-504944564944"), // VPC Stick
            new Guid("205403eb-0000-0000-0000-504944564944"), // VPC Throttle
            new Guid("205603eb-0000-0000-0000-504944564944"), // VPC Throttle
            new Guid("205503eb-0000-0000-0000-504944564944")  // VPC Throttle

        };

        private readonly DirectInput _directInput;
        private readonly Dictionary<Guid, Device> _inputDevices = new Dictionary<Guid, Device>();
        private readonly MainWindow.ToggleOverlayCallback _toggleOverlayCallback;

        private volatile bool _detectPtt;

        //used to trigger the update to a frequency
        private InputBinding _lastActiveBinding = InputBinding.ModifierIntercom
            ; //intercom used to represent null as we cant

        private Settings.GlobalSettingsStore _globalSettings = Settings.GlobalSettingsStore.Instance;


        public InputDeviceManager(Window window, MainWindow.ToggleOverlayCallback _toggleOverlayCallback)
        {
            _directInput = new DirectInput();


            WindowHelper =
                new WindowInteropHelper(window);

            this._toggleOverlayCallback = _toggleOverlayCallback;

            LoadWhiteList();

            LoadBlackList();

            InitDevices();


        }

        public void InitDevices()
        {
            Logger.Info("Starting Device Search. Expand Search: " +
            (_globalSettings.GetClientSettingBool(GlobalSettingsKeys.ExpandControls)));

            var deviceInstances = _directInput.GetDevices();

            foreach (var deviceInstance in deviceInstances)
            {
                //Workaround for Bad Devices that pretend to be joysticks
                if (IsBlackListed(deviceInstance.ProductGuid))
                {
                    Logger.Info("Found but ignoring blacklist device  " + deviceInstance.ProductGuid + " Instance: " +
                        deviceInstance.InstanceGuid + " " +
                        deviceInstance.ProductName.Trim().Replace("\0", "") + " Type: " + deviceInstance.Type);
                    continue;
                }

                Logger.Info("Found Device ID:" + deviceInstance.ProductGuid +
                            " " +
                            deviceInstance.ProductName.Trim().Replace("\0", "") + " Usage: " +
                            deviceInstance.UsagePage + " Type: " +
                            deviceInstance.Type);
                if (_inputDevices.ContainsKey(deviceInstance.InstanceGuid))
                {
                    Logger.Info("Already have device:" + deviceInstance.ProductGuid +
                                " " +
                                deviceInstance.ProductName.Trim().Replace("\0", ""));
                    continue;
                }


                if (deviceInstance.Type == DeviceType.Keyboard)
                {

                    Logger.Info("Adding Device ID:" + deviceInstance.ProductGuid +
                                " " +
                                deviceInstance.ProductName.Trim().Replace("\0", ""));
                    var device = new Keyboard(_directInput);

                    device.SetCooperativeLevel(WindowHelper.Handle,
                        CooperativeLevel.Background | CooperativeLevel.NonExclusive);
                    device.Acquire();

                    _inputDevices.Add(deviceInstance.InstanceGuid, device);
                }
                else if (deviceInstance.Type == DeviceType.Mouse)
                {
                    Logger.Info("Adding Device ID:" + deviceInstance.ProductGuid + " " +
                                deviceInstance.ProductName.Trim().Replace("\0", ""));
                    var device = new Mouse(_directInput);

                    device.SetCooperativeLevel(WindowHelper.Handle,
                        CooperativeLevel.Background | CooperativeLevel.NonExclusive);
                    device.Acquire();

                    _inputDevices.Add(deviceInstance.InstanceGuid, device);
                }
                else if (((deviceInstance.Type >= DeviceType.Joystick) &&
                            (deviceInstance.Type <= DeviceType.FirstPerson)) ||
                            IsWhiteListed(deviceInstance.ProductGuid))
                {
                    var device = new Joystick(_directInput, deviceInstance.InstanceGuid);

                    Logger.Info("Adding ID:" + deviceInstance.ProductGuid + " " +
                                deviceInstance.ProductName.Trim().Replace("\0", ""));

                    device.SetCooperativeLevel(WindowHelper.Handle,
                        CooperativeLevel.Background | CooperativeLevel.NonExclusive);
                    device.Acquire();

                    _inputDevices.Add(deviceInstance.InstanceGuid, device);
                }
                else if (GlobalSettingsStore.Instance.GetClientSettingBool(GlobalSettingsKeys.ExpandControls))
                {
                    Logger.Info("Adding (Expanded Devices) ID:" + deviceInstance.ProductGuid + " " +
                                deviceInstance.ProductName.Trim().Replace("\0", ""));

                    var device = new Joystick(_directInput, deviceInstance.InstanceGuid);

                    device.SetCooperativeLevel(WindowHelper.Handle,
                        CooperativeLevel.Background | CooperativeLevel.NonExclusive);
                    device.Acquire();

                    _inputDevices.Add(deviceInstance.InstanceGuid, device);

                    Logger.Info("Added (Expanded Device) ID:" + deviceInstance.ProductGuid + " " +
                                deviceInstance.ProductName.Trim().Replace("\0", ""));
                }
            }
        }

        private void LoadWhiteList()
        {
            var path = Environment.CurrentDirectory + "\\whitelist.txt";
            Logger.Info("Attempt to Load Whitelist from " + path);

            LoadGuidFromPath(path, _whitelistDevices);
        }

        private void LoadBlackList()
        {
            var path = Environment.CurrentDirectory + "\\blacklist.txt";
            Logger.Info("Attempt to Load Blacklist from " + path);

            LoadGuidFromPath(path, _blacklistedDevices);
        }

        private void LoadGuidFromPath(string path, HashSet<Guid> _hashSet)
        {
            if (!File.Exists(path))
            {
                Logger.Info("File doesnt exist: " + path);
                return;
            }

            string[] lines = File.ReadAllLines(path);
            if (lines?.Length <= 0)
            {
                return;

            }

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.Length > 0)
                {
                    try
                    {
                        _hashSet.Add(new Guid(trimmed));
                        Logger.Info("Added " + trimmed);
                    }
                    catch (Exception ex)
                    {
                    }
                }
            }
        }

        private WindowInteropHelper WindowHelper { get; }


        public void Dispose()
        {
            StopPtt();
            foreach (var kpDevice in _inputDevices)
            {
                if (kpDevice.Value != null)
                {
                    kpDevice.Value.Unacquire();
                    kpDevice.Value.Dispose();
                }
            }
        }

        public bool IsBlackListed(Guid device)
        {
            return _blacklistedDevices.Contains(device);
        }

        public bool IsWhiteListed(Guid device)
        {
            return _whitelistDevices.Contains(device);
        }

        public void AssignButton(DetectButton callback)
        {
            //detect the state of all current buttons
            Task.Run(() =>
            {
                var deviceList = _inputDevices.Values.ToList();

                var initial = new int[deviceList.Count, 128 + 4]; // for POV

                for (var i = 0; i < deviceList.Count; i++)
                {
                    if (deviceList[i] == null || deviceList[i].IsDisposed)
                    {
                        continue;
                    }

                    try
                    {
                        if (deviceList[i] is Joystick)
                        {
                            deviceList[i].Poll();

                            var state = (deviceList[i] as Joystick).GetCurrentState();

                            for (var j = 0; j < state.Buttons.Length; j++)
                            {
                                initial[i, j] = state.Buttons[j] ? 1 : 0;
                            }
                            var pov = state.PointOfViewControllers;

                            for (var j = 0; j < pov.Length; j++)
                            {
                                initial[i, j + 128] = pov[j];
                            }
                        }
                        else if (deviceList[i] is Keyboard)
                        {
                            var keyboard = deviceList[i] as Keyboard;
                            keyboard.Poll();
                            var state = keyboard.GetCurrentState();

                            for (var j = 0; j < 128; j++)
                            {
                                initial[i, j] = state.IsPressed(state.AllKeys[j]) ? 1 : 0;
                            }
                        }
                        else if (deviceList[i] is Mouse)
                        {
                            var mouse = deviceList[i] as Mouse;
                            mouse.Poll();

                            var state = mouse.GetCurrentState();

                            for (var j = 0; j < state.Buttons.Length; j++)
                            {
                                initial[i, j] = state.Buttons[j] ? 1 : 0;
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        Logger.Error(e, $"Failed to get current state of input device {deviceList[i].Information.ProductName.Trim().Replace("\0", "")} " +
                            $"(ID: {deviceList[i].Information.ProductGuid}) while assigning button, ignoring until next restart/rediscovery");

                        deviceList[i].Unacquire();
                        deviceList[i].Dispose();
                        deviceList[i] = null;
                    }
                }

                var device = string.Empty;
                var button = 0;
                var deviceGuid = Guid.Empty;
                var buttonValue = -1;
                var found = false;

                while (!found)
                {
                    Thread.Sleep(100);

                    for (var i = 0; i < _inputDevices.Count; i++)
                    {
                        if (deviceList[i] == null || deviceList[i].IsDisposed)
                        {
                            continue;
                        }

                        try
                        {
                            if (deviceList[i] is Joystick)
                            {
                                deviceList[i].Poll();

                                var state = (deviceList[i] as Joystick).GetCurrentState();

                                for (var j = 0; j < 128 + 4; j++)
                                {
                                    if (j >= 128)
                                    {
                                        //handle POV
                                        var pov = state.PointOfViewControllers;

                                        if (pov[j - 128] != initial[i, j])
                                        {
                                            found = true;

                                            var inputDevice = new InputDevice
                                            {
                                                DeviceName =
                                                    deviceList[i].Information.ProductName.Trim().Replace("\0", ""),
                                                Button = j,
                                                InstanceGuid = deviceList[i].Information.InstanceGuid,
                                                ButtonValue = pov[j - 128]
                                            };
                                            Application.Current.Dispatcher.Invoke(
                                                () => { callback(inputDevice); });
                                            return;
                                        }
                                    }
                                    else
                                    {
                                        var buttonState = state.Buttons[j] ? 1 : 0;

                                        if (buttonState != initial[i, j])
                                        {
                                            found = true;

                                            var inputDevice = new InputDevice
                                            {
                                                DeviceName =
                                                    deviceList[i].Information.ProductName.Trim().Replace("\0", ""),
                                                Button = j,
                                                InstanceGuid = deviceList[i].Information.InstanceGuid,
                                                ButtonValue = buttonState
                                            };

                                            Application.Current.Dispatcher.Invoke(
                                                () => { callback(inputDevice); });


                                            return;
                                        }
                                    }
                                }
                            }
                            else if (deviceList[i] is Keyboard)
                            {
                                var keyboard = deviceList[i] as Keyboard;
                                keyboard.Poll();
                                var state = keyboard.GetCurrentState();

                                for (var j = 0; j < 128; j++)
                                {
                                    if (initial[i, j] != (state.IsPressed(state.AllKeys[j]) ? 1 : 0))
                                    {
                                        found = true;

                                        var inputDevice = new InputDevice
                                        {
                                            DeviceName =
                                                deviceList[i].Information.ProductName.Trim().Replace("\0", ""),
                                            Button = j,
                                            InstanceGuid = deviceList[i].Information.InstanceGuid,
                                            ButtonValue = 1
                                        };

                                        Application.Current.Dispatcher.Invoke(
                                            () => { callback(inputDevice); });


                                        return;
                                    }

                                    //                                if (initial[i, j] == 1)
                                    //                                {
                                    //                                    Console.WriteLine("Pressed: "+j);
                                    //                                    MessageBox.Show("Keyboard!");
                                    //                                }
                                }
                            }
                            else if (deviceList[i] is Mouse)
                            {
                                deviceList[i].Poll();

                                var state = (deviceList[i] as Mouse).GetCurrentState();

                                //skip left mouse button - start at 1 with j 0 is left, 1 is right, 2 is middle
                                for (var j = 1; j < state.Buttons.Length; j++)
                                {
                                    var buttonState = state.Buttons[j] ? 1 : 0;

                                    if (buttonState != initial[i, j])
                                    {
                                        found = true;

                                        var inputDevice = new InputDevice
                                        {
                                            DeviceName =
                                                deviceList[i].Information.ProductName.Trim().Replace("\0", ""),
                                            Button = j,
                                            InstanceGuid = deviceList[i].Information.InstanceGuid,
                                            ButtonValue = buttonState
                                        };

                                        Application.Current.Dispatcher.Invoke(
                                            () => { callback(inputDevice); });
                                        return;
                                    }
                                }
                            }
                        }
                        catch (Exception e)
                        {
                            Logger.Error(e, $"Failed to get current state of input device {deviceList[i].Information.ProductName.Trim().Replace("\0", "")} " +
                                $"(ID: {deviceList[i].Information.ProductGuid}) while discovering button press while assigning, ignoring until next restart/rediscovery");

                            deviceList[i].Unacquire();
                            deviceList[i].Dispose();
                            deviceList[i] = null;
                        }
                    }
                }
            });
        }


        public void StartDetectPtt(DetectPttCallback callback)
        {
            _detectPtt = true;
            //detect the state of all current buttons
            var pttInputThread = new Thread(() =>
            {
                while (_detectPtt)
                {
                    var bindStates = GenerateBindStateList();

                    for (var i = 0; i < bindStates.Count; i++)
                    {
                        //contains main binding and optional modifier binding + states of each
                        var bindState = bindStates[i];

                        bindState.MainDeviceState = GetButtonState(bindState.MainDevice);

                        if (bindState.ModifierDevice != null)
                        {
                            bindState.ModifierState = GetButtonState(bindState.ModifierDevice);

                            bindState.IsActive = bindState.MainDeviceState && bindState.ModifierState;
                        }
                        else
                        {
                            bindState.IsActive = bindState.MainDeviceState;
                        }

                        //now check this is the best binding and no previous ones are better
                        //Means you can have better binds like PTT  = Space and Radio 1 is Space +1 - holding space +1 will actually trigger radio 1 not PTT
                        if (bindState.IsActive)
                        {
                            for (int j = 0; j < i; j++)
                            {
                                //check previous bindings
                                var previousBind = bindStates[j];

                                if (!previousBind.IsActive)
                                {
                                    continue;
                                }

                                if (previousBind.ModifierDevice == null && bindState.ModifierDevice != null)
                                {
                                    //set previous bind to off if previous bind Main == main or modifier of bindstate
                                    if (previousBind.MainDevice.IsSameBind(bindState.MainDevice))
                                    {
                                        previousBind.IsActive = false;
                                        break;
                                    }
                                    if (previousBind.MainDevice.IsSameBind(bindState.ModifierDevice))
                                    {
                                        previousBind.IsActive = false;
                                        break;
                                    }
                                }
                                else if (previousBind.ModifierDevice != null && bindState.ModifierDevice == null)
                                {
                                    if (previousBind.MainDevice.IsSameBind(bindState.MainDevice))
                                    {
                                        bindState.IsActive = false;
                                        break;
                                    }
                                    if (previousBind.ModifierDevice.IsSameBind(bindState.MainDevice))
                                    {
                                        bindState.IsActive = false;
                                        break;
                                    }
                                }
                            }
                        }
                    }

                    callback(bindStates);
                    //handle overlay

                    foreach (var bindState in bindStates)
                    {
                        if (bindState.IsActive && bindState.MainDevice.InputBind == InputBinding.OverlayToggle)
                        {
                            //run on main
                            Application.Current.Dispatcher.Invoke(
                                () => { _toggleOverlayCallback(false); });
                            break;
                        }
                        else if ((int)bindState.MainDevice.InputBind >= (int)InputBinding.RadioChannel1 &&
                                 (int)bindState.MainDevice.InputBind <= (int)InputBinding.RadioChannelDown)
                        {
                            if (bindState.MainDevice.InputBind == _lastActiveBinding && !bindState.IsActive)
                            {
                                //Assign to a totally different binding to mark as unassign
                                _lastActiveBinding = InputBinding.ModifierIntercom;
                            }

                            //key repeat
                            if (bindState.IsActive && (bindState.MainDevice.InputBind != _lastActiveBinding))
                            {
                                _lastActiveBinding = bindState.MainDevice.InputBind;

                                var IL2PlayerRadioInfo = ClientStateSingleton.Instance.PlayerGameState;

                                if (IL2PlayerRadioInfo != null )
                                {
                                    switch (bindState.MainDevice.InputBind)
                                    {
                                        
                                        case InputBinding.RadioChannelUp:

                                            if (!RadioHelper.IsSecondRadioAvailable())
                                            {
                                                RadioHelper.RadioChannelUp(1);
                                            }
                                            else
                                            {
                                                RadioHelper.RadioChannelUp(ClientStateSingleton.Instance.PlayerGameState.selected);
                                            }
                                          
                                            break;
                                        case InputBinding.RadioChannelDown:
                                            if (!RadioHelper.IsSecondRadioAvailable())
                                            {
                                                RadioHelper.RadioChannelDown(1);
                                            }
                                            else
                                            {
                                                RadioHelper.RadioChannelDown(ClientStateSingleton.Instance.PlayerGameState.selected);
                                            }
                                            break;
                                        case InputBinding.RadioChannel1:

                                            if (!RadioHelper.IsSecondRadioAvailable())
                                            {
                                                RadioHelper.SelectRadioChannel(1, 1);
                                            }
                                            else
                                            {
                                                RadioHelper.SelectRadioChannel(1, ClientStateSingleton.Instance.PlayerGameState.selected);
                                            }

                                            break;
                                        case InputBinding.RadioChannel2:
                                            if (!RadioHelper.IsSecondRadioAvailable())
                                            {
                                                RadioHelper.SelectRadioChannel(2, 1);
                                            }
                                            else
                                            {
                                                RadioHelper.SelectRadioChannel(2, ClientStateSingleton.Instance.PlayerGameState.selected);
                                            }
                                            break;
                                        case InputBinding.RadioChannel3:
                                            if (!RadioHelper.IsSecondRadioAvailable())
                                            {
                                                RadioHelper.SelectRadioChannel(3, 1);
                                            }
                                            else
                                            {
                                                RadioHelper.SelectRadioChannel(3, ClientStateSingleton.Instance.PlayerGameState.selected);
                                            }
                                            break;
                                        case InputBinding.RadioChannel4:
                                            if (!RadioHelper.IsSecondRadioAvailable())
                                            {
                                                RadioHelper.SelectRadioChannel(4, 1);
                                            }
                                            else
                                            {
                                                RadioHelper.SelectRadioChannel(4, ClientStateSingleton.Instance.PlayerGameState.selected);
                                            }
                                            break;
                                        case InputBinding.RadioChannel5:
                                            if (!RadioHelper.IsSecondRadioAvailable())
                                            {
                                                RadioHelper.SelectRadioChannel(5, 1);
                                            }
                                            else
                                            {
                                                RadioHelper.SelectRadioChannel(5, ClientStateSingleton.Instance.PlayerGameState.selected);
                                            }
                                            break;
                                        case InputBinding.RadioChannel6:
                                            if (!RadioHelper.IsSecondRadioAvailable())
                                            {
                                                RadioHelper.SelectRadioChannel(6, 1);
                                            }
                                            else
                                            {
                                                RadioHelper.SelectRadioChannel(6, ClientStateSingleton.Instance.PlayerGameState.selected);
                                            }
                                            break;
                                        case InputBinding.RadioChannel7:
                                            if (!RadioHelper.IsSecondRadioAvailable())
                                            {
                                                RadioHelper.SelectRadioChannel(7, 1);
                                            }
                                            else
                                            {
                                                RadioHelper.SelectRadioChannel(7, ClientStateSingleton.Instance.PlayerGameState.selected);
                                            }
                                            break;
                                        case InputBinding.RadioChannel8:
                                            if (!RadioHelper.IsSecondRadioAvailable())
                                            {
                                                RadioHelper.SelectRadioChannel(8, 1);
                                            }
                                            else
                                            {
                                                RadioHelper.SelectRadioChannel(8, ClientStateSingleton.Instance.PlayerGameState.selected);
                                            }
                                            break;
                                        case InputBinding.RadioChannel9:
                                            if (!RadioHelper.IsSecondRadioAvailable())
                                            {
                                                RadioHelper.SelectRadioChannel(9, 1);
                                            }
                                            else
                                            {
                                                RadioHelper.SelectRadioChannel(9, ClientStateSingleton.Instance.PlayerGameState.selected);
                                            }
                                            break;
                                        case InputBinding.RadioChannel10:
                                            if (!RadioHelper.IsSecondRadioAvailable())
                                            {
                                                RadioHelper.SelectRadioChannel(10, 1);
                                            }
                                            else
                                            {
                                                RadioHelper.SelectRadioChannel(10, ClientStateSingleton.Instance.PlayerGameState.selected);
                                            }
                                            break;
                                        case InputBinding.RadioChannel11:
                                            if (!RadioHelper.IsSecondRadioAvailable())
                                            {
                                                RadioHelper.SelectRadioChannel(11, 1);
                                            }
                                            else
                                            {
                                                RadioHelper.SelectRadioChannel(11, ClientStateSingleton.Instance.PlayerGameState.selected);
                                            }
                                            break;
                                        case InputBinding.RadioChannel12:
                                            if (!RadioHelper.IsSecondRadioAvailable())
                                            {
                                                RadioHelper.SelectRadioChannel(12, 1);
                                            }
                                            else
                                            {
                                                RadioHelper.SelectRadioChannel(12, ClientStateSingleton.Instance.PlayerGameState.selected);
                                            }
                                            break;
                                        case InputBinding.PreviousRadio:
                                            RadioHelper.PreviousRadio();
                                            break;
                                        case InputBinding.NextRadio:
                                            RadioHelper.NextRadio();
                                            break;
                                        case InputBinding.ReadStatus:
                                            RadioHelper.ReadStatus();
                                            break;

                                        default:
                                            break;
                                    }
                                }

                                break;
                            }
                        }
                    }

                    Thread.Sleep(40);
                }
            });
            pttInputThread.Start();
        }


        public void StopPtt()
        {
            _detectPtt = false;
        }

        private bool GetButtonState(InputDevice inputDeviceBinding)
        {
            foreach (var kpDevice in _inputDevices)
            {
                var device = kpDevice.Value;
                if (device == null ||
                    device.IsDisposed ||
                    !device.Information.InstanceGuid.Equals(inputDeviceBinding.InstanceGuid))
                {
                    continue;
                }

                try
                {
                    if (device is Joystick)
                    {
                        device.Poll();
                        var state = (device as Joystick).GetCurrentState();

                        if (inputDeviceBinding.Button >= 128) //its a POV!
                        {
                            var pov = state.PointOfViewControllers;
                            //-128 to get POV index
                            return pov[inputDeviceBinding.Button - 128] == inputDeviceBinding.ButtonValue;
                        }
                        else
                        {
                            return state.Buttons[inputDeviceBinding.Button];
                        }
                    }
                    else if (device is Keyboard)
                    {
                        var keyboard = device as Keyboard;
                        keyboard.Poll();
                        var state = keyboard.GetCurrentState();
                        return
                            state.IsPressed(state.AllKeys[inputDeviceBinding.Button]);
                    }
                    else if (device is Mouse)
                    {
                        device.Poll();
                        var state = (device as Mouse).GetCurrentState();

                        //just incase mouse changes number of buttons, like logitech can?
                        if (inputDeviceBinding.Button < state.Buttons.Length)
                        {
                            return state.Buttons[inputDeviceBinding.Button];
                        }
                    }
                }
                catch (Exception e)
                {
                    Logger.Error(e, $"Failed to get current state of input device {device.Information.ProductName.Trim().Replace("\0", "")} " +
                        $"(ID: {device.Information.ProductGuid}) while retrieving button state, ignoring until next restart/rediscovery");

                    MessageBox.Show(
                        $"An error occurred while querying your {device.Information.ProductName.Trim().Replace("\0", "")} input device.\nThis could for example be caused by unplugging " +
                        $"your joystick or disabling it in the Windows settings.\n\nAll controls bound to this input device will not work anymore until your restart SRS.",
                        "Input device error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);

                    device.Unacquire();
                    device.Dispose();
                }

            }
            return false;
        }

        public List<InputBindState> GenerateBindStateList()
        {
            var bindStates = new List<InputBindState>();
            var currentInputProfile = _globalSettings.ProfileSettingsStore.GetCurrentInputProfile();

            //REMEMBER TO UPDATE THIS WHEN NEW BINDINGS ARE ADDED
            //MIN + MAX bind numbers
            for (int i = (int)InputBinding.Intercom; i <= (int)InputBinding.RadioChannelDown; i++)
            {
                if (!currentInputProfile.ContainsKey((InputBinding)i))
                {
                    continue;
                }

                var input = currentInputProfile[(InputBinding)i];
                //construct InputBindState

                var bindState = new InputBindState()
                {
                    IsActive = false,
                    MainDevice = input,
                    MainDeviceState = false,
                    ModifierDevice = null,
                    ModifierState = false
                };

                if (currentInputProfile.ContainsKey((InputBinding)i + 100))
                {
                    bindState.ModifierDevice = currentInputProfile[(InputBinding)i + 100];
                }

                bindStates.Add(bindState);
            }

            return bindStates;
        }
    }
}