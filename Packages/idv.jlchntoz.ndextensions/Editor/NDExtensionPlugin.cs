using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;
using JLChnToZ.NDExtensions.Editors;

[assembly: ExportsPlugin(typeof(NDExtensionPlugin))]

namespace JLChnToZ.NDExtensions.Editors {
    public sealed class NDExtensionPlugin : Plugin<NDExtensionPlugin> {
        public override string QualifiedName => "idv.jlchntoz.ndextension";

        public override string DisplayName => "Vistanz's Non Destructive Plugin";

        protected override void Configure() {
            InPhase(BuildPhase.Resolving)
            .WithRequiredExtension(
                typeof(RebakeHumanoidContext),
                seq => {
                    seq.Run(GetBareFootPass.Instance);
                    seq.Run(ParameterResolverPass.Instance);
                }
            );
            InPhase(BuildPhase.Transforming)
            .AfterPlugin("nadena.dev.modular-avatar")
            .WithRequiredExtension(
                typeof(AnimatorServicesContext),
                seq => {
                    seq.Run(ParameterValueFilterPass.Instance);
                    seq.Run(FeatureMaintainerPass.Instance);
                    seq.Run(ConstraintReducerPass.Instance);
                    seq.WithRequiredExtension(
                        typeof(RebakeHumanoidContext),
                        seq2 => seq2.Run(RebakeHumanoidPass.Instance)
                    );
                }
            );
            InPhase(BuildPhase.Optimizing)
            .BeforePlugin("com.anatawa12.avatar-optimizer")
            .WithRequiredExtension(
                typeof(AnimatorServicesContext),
                seq => seq.Run(ParameterCompressorPass.Instance)
            );
        }
    }
}