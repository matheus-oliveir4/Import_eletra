import type { NextConfig } from "next";

const apiDevOrigin = process.env.API_DEV_ORIGIN ?? "http://localhost:4000";

const nextConfig: NextConfig = {
  async rewrites() {
    if (process.env.NODE_ENV !== "development") return [];

    return [
      { source: "/auth/:path*", destination: `${apiDevOrigin}/auth/:path*` },
      { source: "/api/v1/:path*", destination: `${apiDevOrigin}/api/v1/:path*` },
    ];
  },
};

export default nextConfig;
