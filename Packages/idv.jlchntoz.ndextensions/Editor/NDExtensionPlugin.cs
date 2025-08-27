using nadena.dev.ndmf;
using JLChnToZ.NDExtensions.Editors;

[assembly: ExportsPlugin(typeof(NDExtensionPlugin))]

namespace JLChnToZ.NDExtensions.Editors {
    public sealed class NDExtensionPlugin : Plugin<NDExtensionPlugin> {
        public override string QualifiedName => "idv.jlchntoz.ndextension";

        public override string DisplayName => "Vistanz's Non Destructive Plugin";

        protected override void Configure() {
            InPhase(BuildPhase.Resolving)
            .Run(ParameterResolverPass.Instance).Then
            .Run(GetBareFootPass.Instance);

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