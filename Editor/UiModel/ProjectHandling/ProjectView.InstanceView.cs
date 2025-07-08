#nullable enable
using System.Runtime.CompilerServices;
using T3.Core.Operator;

namespace T3.Editor.UiModel.ProjectHandling;

internal sealed partial class ProjectView
{
    /// <summary>
    /// This class contains logic for the sake of repairing read-only symbols after user modification. It keeps a pool of these objects to avoid garbage,
    /// and a reference count per object, without caching the instance itself for compatibility with runtime compilation.
    ///
    /// In effect, this class only really stores the instance child ID, and its parent symbol Id if it exists.
    /// </summary>
    public sealed class InstanceView : IDisposable
    {
        // runtime-cached fields taken from the instance
        public readonly Guid SymbolChildId;

        // we don't expose these publicly so the Symbol must be fetched, ensuring correctness of any retrieved symbol Id
        private readonly Guid _symbolId;
        private readonly Symbol? _parentSymbol;
        private readonly IReadOnlyList<Guid> _instancePath;

        // true if the symbol's package is read-only (i.e. not an editable project)
        public bool IsReadOnly { get; }
        public int CheckoutCount { get; private set; }

        public bool HasBeenModified => SymbolUi.HasBeenModified;

        // runtime-computed fields for tracking references of this composition
        private bool _disposed;

        /// <summary>
        /// The symbol Ui of the instance this composition refers to
        /// </summary>
        public SymbolUi SymbolUi
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Symbol.GetSymbolUi();
        }

        /// <summary>
        /// The symbol of the instance this composition refers to
        /// </summary>
        /// <exception cref="Exception"></exception>
        public Symbol Symbol
        {
            get
            {
                if (!SymbolRegistry.TryGetSymbol(_symbolId, out var symbol))
                {
                    throw new Exception($"Could not find symbol with id {_symbolId}");
                }

                return symbol;
            }
        }

        /// <summary>
        /// After recompilation (e.g. after adding a new parameter) the Instance can't be
        /// inferred from the parent because it has been discarded and the parent has no
        /// more instances of self. This would lead to an exception in the Instance property.
        ///
        /// Because we want the Instance to be not-null, testing this with the try catch is
        /// somewhat difficult. This is a work-around, but there are probably better ways to
        /// do that. 
        /// </summary>
        public bool IsValid
        {
            get
            {
                try
                {
                    return Instance != null!;
                }
                catch
                {
                    return false;
                }
            }
        }
        

        /// <summary>
        /// Returns the instance this composition refers to
        /// </summary>
        /// <exception cref="Exception"></exception>
        public Instance Instance
        {
            get
            {
                if(Symbol.TryGetOrCreateInstance(_instancePath, _parentSymbol, out var instance))
                {
                    return instance;
                }
                
                throw new Exception($"Could not find instance with id {_symbolId} and child id {SymbolChildId}. Symbol type: \"{Symbol.InstanceType}\"");
            }
        }

        private InstanceView(Instance instance)
        {
            _instancePath = instance.InstancePath;
            var parent = instance.Parent;
            if (parent != null)
            {
                _parentSymbol = parent.Symbol;
            }

            SymbolChildId = instance.SymbolChildId;
            var symbol = instance.Symbol;
            IsReadOnly = symbol.SymbolPackage.IsReadOnly;
            _symbolId = symbol.Id;
        }

        internal static InstanceView GetForInstance(Instance instance)
        {
            if (instance.IsDisposed)
            {
                throw new InvalidOperationException("Cannot get composition for disposed instance.");
            }

            InstanceView? composition;
            var childId = instance.SymbolChildId;
            var key = new InstanceViewKey(childId, instance.Parent?.Symbol.Id);
            lock (_viewsBySymbolChildId)
            {
                if (!_viewsBySymbolChildId.TryGetValue(key, out composition))
                {
                    composition = new InstanceView(instance);
                    _viewsBySymbolChildId[key] = composition;
                }

                composition.CheckoutCount++;
            }

            return composition;
        }

        public bool Is(Instance newCompositionOp)
        {
            var othersKey = new InstanceViewKey(newCompositionOp.SymbolChildId, newCompositionOp.Parent?.Symbol.Id);
            var myKey = new InstanceViewKey(SymbolChildId, _parentSymbol?.Id);
            return othersKey == myKey;
        }

        public void Dispose()
        {
            CheckoutCount--;
            if (CheckoutCount > 0)
                return;

            if (_disposed)
                throw new Exception("Composition already disposed.");

            _disposed = true;

            lock (_viewsBySymbolChildId)
            {
                var key = new InstanceViewKey(SymbolChildId, _parentSymbol?.Id);
                _viewsBySymbolChildId.Remove(key);
            }
        }

        /// <summary>
        /// A key for looking up the relevant <see cref="InstanceView"/> object
        /// Requires the parent symbol ID as a symbol can have multiple live instances with the same SymbolChildId if they belong to multiple
        /// instances of the same parent
        /// </summary>
        /// <param name="SymbolChildId">The <see cref="Symbol.Child.Id"/> of this instance reference</param>
        /// <param name="ParentSymbolId">The symbol id of the parent, if it exists</param>
        private readonly record struct InstanceViewKey(Guid SymbolChildId, Guid? ParentSymbolId);

        private static readonly Dictionary<InstanceViewKey, InstanceView> _viewsBySymbolChildId = new();
    }
}