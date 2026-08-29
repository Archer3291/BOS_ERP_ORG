using BOS_ERP.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using Microsoft.AspNetCore.Mvc;
using static BOS_ERP.Controllers.SqlHelper;

namespace BOS_ERP.Controllers
{
    [RightAuthorize("sistemas")]
    public class SettingsController : Utilities
    {
        public IActionResult Settings()
        {
            string query = "SELECT id_setting, display_name, setting_name, setting_value, setting_type FROM settings";
            var result = RunQuery(query);
            var settings = new List<SettingsManagement>();
            foreach (var item in result)
            {
                SettingsManagement settingsManagement = new SettingsManagement();
                settingsManagement.id_setting = (int)item["id_setting"];
                settingsManagement.displayName = item["display_name"].ToString();
                settingsManagement.settingName = item["setting_name"].ToString();
                settingsManagement.settingValue = item["setting_value"].ToString();
                settingsManagement.settingType = item["setting_type"].ToString();
                settings.Add(settingsManagement);
            }
            return View(settings);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public IActionResult Settings(List<SettingsManagement> settings)
        {
            string query = "UPDATE settings SET setting_value = @settingValue WHERE setting_name = @settingName";

            foreach (var item in settings)
            {
                object valorConvertido = ConvertirValorSetting(item.settingValue, item.settingType);

                Dictionary<string, object> parameters = new Dictionary<string, object>();
                parameters.Add("settingName", item.settingName);
                parameters.Add("settingValue", valorConvertido);
                RunUpdate(query, parameters);
            }

            return Settings();
        }

        private object ConvertirValorSetting(string valor, string tipo)
        {
            switch (tipo?.ToLower())
            {
                case "checkbox":
                    // El checkbox puede llegar como "true", "false", "true,false" o null
                    bool esTrue = !string.IsNullOrEmpty(valor) && valor.Split(',')[0].Trim().ToLower() == "true";
                    return esTrue;

                case "number":
                    return decimal.TryParse(valor, out var num) ? (object)num : (object)0;

                default:
                    return valor ?? "";
            }
        }
    }
}