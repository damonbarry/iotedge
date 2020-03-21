// Example:
//
//     cargo run --example publisher -- --server 127.0.0.1:1883 --client-id 'example-publisher' --publish-frequency 1000 --topic foo --qos 1 --payload 'hello, world'

#![allow(clippy::let_unit_value)]

mod common;

use std::convert::TryInto;
use opentelemetry::{api::{Key, Provider, Span, TracerGenerics, TRACE_FLAG_SAMPLED}, global, sdk};

static SUPPORTED_VERSION: u8 = 0;

#[derive(Debug, structopt::StructOpt)]
struct Options {
    #[structopt(help = "Address of the MQTT server.", long = "server")]
    server: std::net::SocketAddr,

    #[structopt(
        help = "Client ID used to identify this application to the server. If not given, a server-generated ID will be used.",
        long = "client-id"
    )]
    client_id: Option<String>,

    #[structopt(
        help = "Username used to authenticate with the server, if any.",
        long = "username"
    )]
    username: Option<String>,

    #[structopt(
        help = "Password used to authenticate with the server, if any.",
        long = "password"
    )]
    password: Option<String>,

    #[structopt(
		help = "Maximum back-off time between reconnections to the server, in seconds.",
		long = "max-reconnect-back-off",
		default_value = "30",
		parse(try_from_str = common::duration_from_secs_str),
	)]
    max_reconnect_back_off: std::time::Duration,

    #[structopt(
		help = "Keep-alive time advertised to the server, in seconds.",
		long = "keep-alive",
		default_value = "5",
		parse(try_from_str = common::duration_from_secs_str),
	)]
    keep_alive: std::time::Duration,

    #[structopt(
		help = "How often to publish to the server, in milliseconds.",
		long = "publish-frequency",
		default_value = "1000",
		parse(try_from_str = duration_from_millis_str),
	)]
    publish_frequency: std::time::Duration,

    #[structopt(help = "The topic of the publications.", long = "topic")]
    topic: String,

    #[structopt(help = "The QoS of the publications.", long = "qos", parse(try_from_str = common::qos_from_str))]
    qos: mqtt3::proto::QoS,

    #[structopt(help = "The payload of the publications.", long = "payload")]
    payload: String,
}

fn init_tracer() -> thrift::Result<()> {
    let exporter = opentelemetry_jaeger::Exporter::builder()
        .with_agent_endpoint("127.0.0.1:6831".parse().unwrap())
        .with_process(opentelemetry_jaeger::Process {
            service_name: "mqtt_publisher".to_string(),
            tags: vec![],
        })
        .init()?;
    let provider = sdk::Provider::builder()
        .with_simple_exporter(exporter)
        .with_config(sdk::Config {
            default_sampler: Box::new(sdk::Sampler::Always),
            ..Default::default()
        })
        .build();
    global::set_provider(provider);

    Ok(())
}

fn main() {
    env_logger::Builder::from_env(env_logger::Env::new().filter_or(
        "MQTT3_LOG",
        "mqtt3=debug,mqtt3::logging=trace,publisher=info",
    ))
    .init();

    init_tracer().expect("couldn't initialize tracer");
    let tracer = global::trace_provider().get_tracer("publisher-main");

    let Options {
        server,
        client_id,
        username,
        password,
        max_reconnect_back_off,
        keep_alive,
        publish_frequency,
        topic,
        qos,
        payload,
    } = structopt::StructOpt::from_args();

    let mut runtime = tokio::runtime::Runtime::new().expect("couldn't initialize tokio runtime");
    let runtime_handle = runtime.handle().clone();

    let mut client = mqtt3::Client::new(
        client_id,
        username,
        None,
        move || {
            let password = password.clone();
            Box::pin(async move {
                let io = tokio::net::TcpStream::connect(&server).await;
                io.map(|io| (io, password))
            })
        },
        max_reconnect_back_off,
        keep_alive,
    );

    let mut shutdown_handle = client
        .shutdown_handle()
        .expect("couldn't get shutdown handle");
    runtime.spawn(async move {
        let () = tokio::signal::ctrl_c()
            .await
            .expect("couldn't get Ctrl-C notification");
        let result = shutdown_handle.shutdown().await;
        let () = result.expect("couldn't send shutdown notification");
    });

    let payload: bytes::Bytes = payload.into();

    let publish_handle = client
        .publish_handle()
        .expect("couldn't get publish handle");
    runtime_handle.clone().spawn(async move {
        use futures_util::StreamExt;

        let mut interval = tokio::time::interval(publish_frequency).enumerate();
        while let Some((i, _)) = interval.next().await {
            tracer.with_span("publish", |span| {
                span.set_attribute(Key::from("iteration").u64(i.try_into().unwrap()));

                let context = span.get_context();
                let traceparent = format!(
                    "{:02x}-{:032x}-{:016x}-{:02x}",
                    SUPPORTED_VERSION,
                    context.trace_id(),
                    context.span_id(),
                    context.trace_flags() & TRACE_FLAG_SAMPLED    
                );

                let topic = format!("{}/traceparent={}", topic, traceparent);
                log::info!("Publishing to {} ...", topic);

                let mut publish_handle = publish_handle.clone();
                let payload = payload.clone();
                runtime_handle.spawn(async move {
                    publish_handle
                        .publish(mqtt3::proto::Publication {
                            topic_name: topic.clone(),
                            qos,
                            retain: false,
                            payload,
                        })
                        .await
                        .expect("couldn't publish");
                    log::info!("Published to {}", topic);
                    Ok::<_, ()>(())
                });
            });
        }
    });

    let () = runtime.block_on(async {
        use futures_util::StreamExt;

        while let Some(event) = client.next().await {
            let _ = event.unwrap();
        }
    });
}

fn duration_from_millis_str(
    s: &str,
) -> Result<std::time::Duration, <u64 as std::str::FromStr>::Err> {
    Ok(std::time::Duration::from_millis(s.parse()?))
}
