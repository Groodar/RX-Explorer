﻿using RX_Explorer.Class;
using System;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.UI.Xaml;

namespace RX_Explorer
{
    public static class Program
    {
        static void Main(string[] args)
        {
            IActivatedEventArgs activatedArgs = AppInstance.GetActivatedEventArgs();

            if (activatedArgs is ToastNotificationActivatedEventArgs)
            {
                return;
            }

            if (AppInstance.GetInstances().Count == 0)
            {
                AppInstanceIdContainer.ClearAll();
            }

            if (activatedArgs is CommandLineActivatedEventArgs CmdActivate)
            {
                if (CmdActivate.Operation.Arguments.StartsWith("RX-Explorer.exe"))
                {
                    if (AppInstance.RecommendedInstance != null)
                    {
                        AppInstance.RecommendedInstance.RedirectActivationTo();
                    }
                    else if (!string.IsNullOrWhiteSpace(AppInstanceIdContainer.LastActiveId)
                             && AppInstance.FindOrRegisterInstanceForKey(AppInstanceIdContainer.LastActiveId) is AppInstance TargetInstance
                             && !TargetInstance.IsCurrentInstance)
                    {
                        TargetInstance.RedirectActivationTo();
                    }
                    else
                    {
                        string InstanceId = Guid.NewGuid().ToString();
                        AppInstance Instance = AppInstance.FindOrRegisterInstanceForKey(InstanceId);
                        AppInstanceIdContainer.RegisterCurrentId(InstanceId);

                        Application.Start((p) => new App());
                    }
                }
                else
                {
                    string InstanceId = Guid.NewGuid().ToString();
                    AppInstance Instance = AppInstance.FindOrRegisterInstanceForKey(InstanceId);
                    AppInstanceIdContainer.RegisterCurrentId(InstanceId);

                    Application.Start((p) => new App());
                }
            }
            else
            {
                string InstanceId = Guid.NewGuid().ToString();
                AppInstance Instance = AppInstance.FindOrRegisterInstanceForKey(InstanceId);
                AppInstanceIdContainer.RegisterCurrentId(InstanceId);

                Application.Start((p) => new App());
            }
        }
    }
}
