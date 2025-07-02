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
                seq => seq.Run(GetBareFootPass.Instance)
            );
            InPhase(BuildPhase.Transforming)
            .AfterPlugin("nadena.dev.modular-avatar")
            .WithRequiredExtension(
                typeof(AnimatorServicesContext),
                seq => {
                    seq.WithRequiredExtension(
                        typeof(RebakeHumanoidContext),
                        seq2 => seq2.Run(RebakeHumanoidPass.Instance)
                    );
                    seq.Run(FeatureMaintainerPass.Instance);
                }
            );
            InPhase(BuildPhase.Optimizing)
            .AfterPlugin("nadena.dev.modular-avatar")
            .BeforePlugin("com.anatawa12.avatar-optimizer")
            .WithRequiredExtension(
                typeof(AnimatorServicesContext),
                seq => seq.Run(ConstraintReducerPass.Instance)
            );
            InPhase(BuildPhase.Optimizing)
            .AfterPlugin("com.anatawa12.avatar-optimizer")
            .AfterPlugin("nadena.dev.modular-avatar")
            .Run(DeepCleanupPass.Instance);
        }
    }
}