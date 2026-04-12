import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui-kits/card/card";

export default function ApiSettingsPage() {
  return (
    <main className="flex flex-col gap-6 p-6">
      <div>
        <h1 className="text-xl font-semibold md:text-2xl">API Settings</h1>
        <p className="text-muted-foreground">Manage your API keys, endpoints, and configurations</p>
      </div>

      <Card>
        <CardHeader>
          <CardTitle>API Settings</CardTitle>
          <CardDescription>Configure your API settings and preferences</CardDescription>
        </CardHeader>
        <CardContent className="flex h-40 items-center justify-center text-muted-foreground">
          API Settings content coming soon...
        </CardContent>
      </Card>
    </main>
  );
}
