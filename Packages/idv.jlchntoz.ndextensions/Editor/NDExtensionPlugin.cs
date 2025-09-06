using nadena.dev.ndmf;
using JLChnToZ.NDExtensions.Editors;
using UnityEngine;

[assembly: ExportsPlugin(typeof(NDExtensionPlugin))]

namespace JLChnToZ.NDExtensions.Editors {
    public sealed class NDExtensionPlugin : Plugin<NDExtensionPlugin> {
        public override string QualifiedName => "idv.jlchntoz.ndextension";

        public override string DisplayName => "Vistanz's Non Destructive Plugin";

        public override Color? ThemeColor => new(1, 0.65f, 0);

        protected override void Configure() {
            InPhase(BuildPhase.Resolving)
            .Run(ParameterResolverEarlyPass.Instance).Then
            .Run(GetBareFootPass.Instance);

            InPhase(BuildPhase.Transforming)
            .BeforePlugin("nadena.dev.modular-avatar")
            .Run(ParameterResolverPass.Instance);

            InPhase(BuildPhase.Transforming)
            .AfterPlugin("nadena.dev.modular-avatar")
            .Run(FeatureMaintainerPass.Instance).Then
            .Run(ParameterValueFilterPass.Instance).Then
            .Run(ConstraintReducerPass.Instance).Then
            .Run(RebakeHumanoidPass.Instance).Then
            .Run(ParameterCompressorPrePass.Instance);

            InPhase(BuildPhase.Optimizing)
            .BeforePlugin("com.anatawa12.avatar-optimizer")
            .Run(ParameterCompressorPass.Instance);
        }
    }
}