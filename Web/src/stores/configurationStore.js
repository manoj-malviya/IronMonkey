import axios from "axios";
import { defineStore } from "pinia";

export const useConfigurationStore = defineStore('cnfigurationStore', () => {
    const basePath = `http://localhost:5002`;

    async function saveFormDefinition(form) {
        return await axios.post(`${basePath}/form-definition`, form);
    }

    return {
        saveFormDefinition
    }
});