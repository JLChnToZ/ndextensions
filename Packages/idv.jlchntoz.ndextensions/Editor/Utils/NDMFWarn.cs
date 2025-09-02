using System;
using nadena.dev.ndmf;
using nadena.dev.ndmf.localization;
#if VRC_SDK_VRCSDK3
#endif

namespace JLChnToZ.NDExtensions.Editors {
    class NDMFWarn : SimpleError {
        public override Localizer Localizer => null;
        public override string TitleKey => title;
        public override ErrorSeverity Severity => ErrorSeverity.NonFatal;
        private readonly string title;
        public string[] titleSubSt, detailsSubSt, hintSubSt;

        public override string[] TitleSubst => titleSubSt ?? Array.Empty<string>();
        public override string[] DetailsSubst => detailsSubSt ?? Array.Empty<string>();
        public override string[] HintSubst => hintSubSt ?? Array.Empty<string>();

        public NDMFWarn(string title) {
            this.title = title;
        }

        public override string FormatTitle() => SafeSubst(I18N.Instance[TitleKey] ?? TitleKey, TitleSubst);

        public override string FormatDetails() => SafeSubst(I18N.Instance[DetailsKey] ?? DetailsKey, DetailsSubst);

        public override string FormatHint() => SafeSubst(I18N.Instance[HintKey] ?? HintKey, HintSubst);
    }
}
