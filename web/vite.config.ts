import { defineConfig } from "vite"; import react from "@vitejs/plugin-react";
export default defineConfig({ plugins: [react()], server: { proxy: { "/catalog-api": { target: "http://localhost:5001", rewrite: p => p.replace("/catalog-api", "") }, "/orders-api": { target: "http://localhost:5002", rewrite: p => p.replace("/orders-api", "") } } } });
