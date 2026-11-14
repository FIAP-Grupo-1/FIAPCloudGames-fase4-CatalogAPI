using CatalogAPI.Application.Contexts.Games.UseCases.Search.DTOs;
using Elastic.Clients.Elasticsearch.Inference;
using Flunt.Notifications;
using System;
using System.Collections.Generic;
using System.Text;

namespace CatalogAPI.Application.Contexts.Games.UseCases.Search
{
    public class Response : Shared.Response
    {
        #region Properties

        public SearchGameResponse? Data { get; private set; }

        #endregion

        #region Constructors

        protected Response() { }

        public Response(string message, SearchGameResponse data)
        {
            Message = message;
            Data = data;
            StatusCode = 200;
        }

        public Response(
            string message,
            int statusCode,
            IEnumerable<Notification>? notifications = null)
        {
            Message = message;
            StatusCode = statusCode;
            Notifications = notifications;
        }

        #endregion
    }
}
