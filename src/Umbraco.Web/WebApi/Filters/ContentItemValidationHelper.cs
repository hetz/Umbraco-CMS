﻿using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Web.Http.Controllers;
using Umbraco.Core;
using Umbraco.Core.Logging;
using Umbraco.Core.Models;
using Umbraco.Web.Composing;
using Umbraco.Web.Models.ContentEditing;

namespace Umbraco.Web.WebApi.Filters
{
    /// <summary>
    /// A validation helper class used with ContentItemValidationFilterAttribute to be shared between content, media, etc...
    /// </summary>
    /// <typeparam name="TPersisted"></typeparam>
    /// <typeparam name="TModelSave"></typeparam>
    /// <remarks>
    /// If any severe errors occur then the response gets set to an error and execution will not continue. Property validation
    /// errors will just be added to the ModelState.
    /// </remarks>
    internal class ContentItemValidationHelper<TPersisted, TModelSave>
        where TPersisted : class, IContentBase
        where TModelSave : ContentBaseItemSave<TPersisted>
    {

        public void ValidateItem(HttpActionContext actionContext, string argumentName)
        {
            var contentItem = actionContext.ActionArguments[argumentName] as TModelSave;
            if (contentItem == null)
            {
                actionContext.Response = actionContext.Request.CreateErrorResponse(HttpStatusCode.BadRequest, "No " + typeof(TModelSave) + " found in request");
                return;
            }

            ValidateItem(actionContext, contentItem);

        }

        public void ValidateItem(HttpActionContext actionContext, TModelSave contentItem)
        {
            //now do each validation step
            if (ValidateExistingContent(contentItem, actionContext) == false) return;
            if (ValidateProperties(contentItem, actionContext) == false) return;
            if (ValidatePropertyData(contentItem, actionContext) == false) return;
        }

        /// <summary>
        /// Ensure the content exists
        /// </summary>
        /// <param name="postedItem"></param>
        /// <param name="actionContext"></param>
        /// <returns></returns>
        protected virtual bool ValidateExistingContent(ContentItemBasic<ContentPropertyBasic, TPersisted> postedItem, HttpActionContext actionContext)
        {
            if (postedItem.PersistedContent == null)
            {
                var message = string.Format("content with id: {0} was not found", postedItem.Id);
                actionContext.Response = actionContext.Request.CreateErrorResponse(HttpStatusCode.NotFound, message);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Ensure all of the ids in the post are valid
        /// </summary>
        /// <param name="postedItem"></param>
        /// <param name="actionContext"></param>
        /// <returns></returns>
        protected virtual bool ValidateProperties(ContentItemBasic<ContentPropertyBasic, TPersisted> postedItem, HttpActionContext actionContext)
        {
            return ValidateProperties(postedItem.Properties.ToList(), postedItem.PersistedContent.Properties.ToList(), actionContext);
        }

        /// <summary>
        /// This validates that all of the posted properties exist on the persisted entity
        /// </summary>
        /// <param name="postedProperties"></param>
        /// <param name="persistedProperties"></param>
        /// <param name="actionContext"></param>
        /// <returns></returns>
        protected bool ValidateProperties(List<ContentPropertyBasic> postedProperties , List<Property> persistedProperties, HttpActionContext actionContext)
        {
            foreach (var p in postedProperties)
            {
                if (persistedProperties.Any(property => property.Alias == p.Alias) == false)
                {
                    //TODO: Do we return errors here ? If someone deletes a property whilst their editing then should we just
                    //save the property data that remains? Or inform them they need to reload... not sure. This problem exists currently too i think.

                    var message = string.Format("property with alias: {0} was not found", p.Alias);
                    actionContext.Response = actionContext.Request.CreateErrorResponse(HttpStatusCode.NotFound, new InvalidOperationException(message));
                    return false;
                }

            }
            return true;
        }

        /// <summary>
        /// Validates the data for each property
        /// </summary>
        /// <param name="postedItem"></param>
        /// <param name="actionContext"></param>
        /// <returns></returns>
        /// <remarks>
        /// All property data validation goes into the modelstate with a prefix of "Properties"
        /// </remarks>
        protected virtual bool ValidatePropertyData(ContentItemBasic<ContentPropertyBasic, TPersisted> postedItem, HttpActionContext actionContext)
        {
            foreach (var p in postedItem.ContentDto.Properties)
            {
                var editor = p.PropertyEditor;

                if (editor == null)
                {
                    var message = $"Could not find property editor \"{p.DataType.EditorAlias}\" for property with id {p.Id}.";
                    Current.Logger.Warn<ContentItemValidationHelper<TPersisted, TModelSave>>(message);
                    //actionContext.Response = actionContext.Request.CreateErrorResponse(HttpStatusCode.NotFound, message);
                    //return false;
                    continue;
                }

                //get the posted value for this property, this may be null in cases where the property was marked as readonly which means
                //the angular app will not post that value.
                var postedProp = postedItem.Properties.FirstOrDefault(x => x.Alias == p.Alias);
                if (postedProp == null) continue;

                var postedValue = postedProp.Value;

                // validate
                var valueEditor = editor.GetValueEditor(p.DataType.Configuration);
                foreach (var r in valueEditor.Validate(postedValue, p.IsRequired, p.ValidationRegExp))
                    actionContext.ModelState.AddPropertyError(r, p.Alias);
            }

            return actionContext.ModelState.IsValid;
        }


    }
}
