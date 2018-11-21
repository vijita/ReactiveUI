﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reflection;
using System.Windows.Input;

namespace ReactiveUI
{
    /// <summary>
    /// Command binder for android controls.
    /// </summary>
    public abstract class FlexibleCommandBinder : ICreatesCommandBinding
    {
        /// <inheritdoc/>
        public int GetAffinityForObject(Type type, bool hasEventTarget)
        {
            if (hasEventTarget)
            {
                return 0;
            }

            var match = _config.Keys
                .Where(x => x.IsAssignableFrom(type))
                .OrderByDescending(x => _config[x].Affinity)
                .FirstOrDefault();

            if (match == null)
            {
                return 0;
            }

            var typeProperties = _config[match];
            return typeProperties.Affinity;
        }

        /// <inheritdoc/>
        public IDisposable BindCommandToObject(ICommand command, object target, IObservable<object> commandParameter)
        {
            var type = target.GetType();

            var match = _config.Keys
                .Where(x => x.IsAssignableFrom(type))
                .OrderByDescending(x => _config[x].Affinity)
                .FirstOrDefault();

            if (match == null)
            {
                throw new NotSupportedException(string.Format("CommandBinding for {0} is not supported", type.Name));
            }

            var typeProperties = _config[match];

            return typeProperties.CreateBinding(command, target, commandParameter);
        }

        /// <inheritdoc/>
        public IDisposable BindCommandToObject<TEventArgs>(ICommand command, object target, IObservable<object> commandParameter, string eventName)
#if MONO
            where TEventArgs : EventArgs
#endif
        {
            throw new NotImplementedException();
        }

        private class CommandBindingInfo
        {
            public int Affinity { get; set; }

            public Func<ICommand, object, IObservable<object>, IDisposable> CreateBinding { get; set; }
        }

        /// <summary>
        /// Configuration map.
        /// </summary>
        private readonly Dictionary<Type, CommandBindingInfo> _config = new Dictionary<Type, CommandBindingInfo>();

        /// <summary>
        /// Registers an observable factory for the specified type and property.
        /// </summary>
        /// <param name="type">Type.</param>
        /// <param name="affinity">The affinity for the type.</param>
        /// <param name="createBinding">Creates the binding.</param>
        protected void Register(Type type, int affinity, Func<System.Windows.Input.ICommand, object, IObservable<object>, IDisposable> createBinding)
        {
            _config[type] = new CommandBindingInfo { Affinity = affinity, CreateBinding = createBinding };
        }

        /// <summary>
        /// Creates a commands binding from event and a property.
        /// </summary>
        /// <returns>The binding from event.</returns>
        /// <param name="command">Command.</param>
        /// <param name="target">Target.</param>
        /// <param name="commandParameter">Command parameter.</param>
        /// <param name="eventName">Event name.</param>
        /// <param name="enabledProperty">Enabled property name.</param>
        protected static IDisposable ForEvent(ICommand command, object target, IObservable<object> commandParameter, string eventName, PropertyInfo enabledProperty)
        {
            commandParameter = commandParameter ?? Observable.Return(target);

            object latestParam = null;
            var ctl = target;

            var actionDisp = Observable.FromEventPattern(ctl, eventName).Subscribe((e) =>
            {
                if (command.CanExecute(latestParam))
                {
                    command.Execute(latestParam);
                }
            });

            var enabledSetter = Reflection.GetValueSetterForProperty(enabledProperty);
            if (enabledSetter == null)
            {
                return actionDisp;
            }

            // initial enabled state
            enabledSetter(target, command.CanExecute(latestParam), null);

            var compDisp = new CompositeDisposable(
                actionDisp,
                commandParameter.Subscribe(x => latestParam = x),
                Observable.FromEventPattern<EventHandler, EventArgs>(x => command.CanExecuteChanged += x, x => command.CanExecuteChanged -= x)
                    .Select(_ => command.CanExecute(latestParam))
                    .Subscribe(x => enabledSetter(target, x, null)));

            return compDisp;
        }
    }
}