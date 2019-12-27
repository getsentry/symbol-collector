using System;
using System.ComponentModel.DataAnnotations;

namespace SymbolCollector.Server
{
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
    public class NotEmptyGuidAttribute : ValidationAttribute
    {
        private const string DefaultErrorMessage = "The {0} field must not be empty";
        public NotEmptyGuidAttribute() : base(DefaultErrorMessage) { }

        public override bool IsValid(object value) =>
            value switch
            {
                Guid guid => (guid != Guid.Empty),
                _ => true
            };
    }

}
