const token = () =>
  sessionStorage.getItem("access_token") ??
  import.meta.env.VITE_DEV_TOKEN ?? "";
